using CarScanner.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CarScanner.Tests;

/// <summary>
/// The accountless (no OBD360 account) App Store purchase, server side.
///
/// App Review rejected build 56 under Guideline 5.1.1(v) because the subscription
/// could not be bought without registering. The fix keeps the existing, strong
/// App Store Server API verification and changes only *ownership*: an
/// "anon:" owner plus an opaque session token whose SHA-256 hash is all that is
/// stored. These tests exercise the real <see cref="EntitlementStore"/> against a
/// throw-away SQLite database, so the token, the claim rules and the entitlement
/// mapping are verified by running code rather than by reading it.
/// </summary>
public sealed class AccountlessApplePurchaseTests : IDisposable
{
    private readonly string _directory;

    public AccountlessApplePurchaseTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "gcar360-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { }
    }

    private EntitlementStore NewStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DataDirectory"] = _directory
            })
            .Build();

        return new EntitlementStore(new TestHost(_directory), configuration);
    }

    private sealed class TestHost : IHostEnvironment
    {
        public TestHost(string root)
        {
            ContentRootPath = root;
            ContentRootFileProvider = new PhysicalFileProvider(root);
        }

        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "CarScanner.Tests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }

    // ============================================================
    // SESSION TOKEN
    // ============================================================

    [Fact]
    public async Task ASessionToken_ResolvesToItsOwner()
    {
        var store = NewStore();
        var owner = EntitlementStore.NewAnonymousOwnerId();

        var token = await store.CreateAnonymousSessionAsync(owner, "install-1");

        Assert.Equal(owner, await store.ResolveAnonymousOwnerAsync(token));
    }

    [Fact]
    public async Task AnUnknownOrEmptyToken_ResolvesToNothing()
    {
        var store = NewStore();
        await store.CreateAnonymousSessionAsync(EntitlementStore.NewAnonymousOwnerId(), null);

        Assert.Null(await store.ResolveAnonymousOwnerAsync("not-a-real-token"));
        Assert.Null(await store.ResolveAnonymousOwnerAsync(""));
        Assert.Null(await store.ResolveAnonymousOwnerAsync(null));
    }

    [Fact]
    public async Task TheRawTokenIsNeverWrittenToTheDatabase()
    {
        var store = NewStore();
        var token = await store.CreateAnonymousSessionAsync(EntitlementStore.NewAnonymousOwnerId(), null);

        using var connection = new SqliteConnection(
            BackendDataPath.CreateConnectionString(
                Path.Combine(_directory, "car-scanner-api.db")));
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TokenHash FROM AnonymousStoreSessions LIMIT 1;";
        var stored = command.ExecuteScalar() as string;

        Assert.NotNull(stored);
        Assert.NotEqual(token, stored);
        Assert.Equal(64, stored!.Length);                       // SHA-256 hex
        Assert.Equal(EntitlementStore.AnonymousTokenHash(token), stored);
    }

    [Fact]
    public void AnAnonymousOwner_NeverLooksLikeAnAccount()
    {
        var anonymous = EntitlementStore.NewAnonymousOwnerId();

        Assert.StartsWith("anon:", anonymous);
        Assert.True(EntitlementStore.IsAnonymousOwner(anonymous));

        // A real account id is a GUID and must never be treated as accountless.
        Assert.False(EntitlementStore.IsAnonymousOwner(Guid.NewGuid().ToString("N")));
        Assert.False(EntitlementStore.IsAnonymousOwner(null));
    }

    // ============================================================
    // TRANSACTION OWNERSHIP
    // ============================================================

    [Fact]
    public async Task OneTransaction_BelongsToOneAccount()
    {
        // PurchaseClaims is the *account* ownership rule and stays exclusive.
        // Accountless installations are never written there; they share the
        // subscription through StoreEntitlementLinks instead (see below).
        var store = NewStore();
        var first = Guid.NewGuid().ToString("N");
        var second = Guid.NewGuid().ToString("N");

        Assert.True(await store.TryClaimTransactionAsync("apple", "OTID-1", first, false));
        Assert.False(await store.TryClaimTransactionAsync("apple", "OTID-1", second, false));
        Assert.Equal(first, await store.FindClaimOwnerAsync("apple", "OTID-1"));
    }

    // ============================================================
    // ONE APPLE ACCOUNT, SEVERAL DEVICES
    //
    // An App Store subscription belongs to the customer's Apple Account, not to
    // one installation. Restoring it on a second device (or after a reinstall)
    // must give that device access WITHOUT revoking the first.
    // ============================================================

    /// <summary>Exactly what POST /api/store/apple/anonymous/verify does after Apple verified the subscription.</summary>
    private static async Task LinkDeviceAsync(
        EntitlementStore store,
        string ownerId,
        string subscriptionKey,
        string productId,
        DateTimeOffset? expiresAt)
    {
        await store.LinkSubscriptionOwnerAsync("apple", subscriptionKey, ownerId);
        await store.UpsertSubscriptionAsync(
            new EntitlementRecord(
                ownerId,
                true,
                "apple",
                productId,
                expiresAt ?? DateTimeOffset.UtcNow.AddDays(30),
                DateTimeOffset.UtcNow,
                subscriptionKey));
    }

    /// <summary>
    /// Mirrors POST /api/store/apple/anonymous/verify for ONE device: the device
    /// presents its own StoreKit transaction id, Apple resolves it to the
    /// subscription's originalTransactionId, and the server keys everything by
    /// that original transaction id.
    /// </summary>
    private static async Task<VerifiedPurchase> VerifyOnDeviceAsync(
        EntitlementStore store,
        string ownerId,
        string ownTransactionId,
        string originalTransactionId,
        DateTimeOffset? expiresAt = null)
    {
        var verified = new VerifiedPurchase(
            ProductCatalog.FullMonthly,
            expiresAt ?? DateTimeOffset.UtcNow.AddDays(30),
            originalTransactionId,   // VerifyAppleAsync returns the resolved original transaction id
            originalTransactionId);

        // The subscription key is the original transaction id, never the
        // per-transaction id of the device that happened to present it.
        Assert.Equal(originalTransactionId, verified.SubscriptionKey);

        await store.LinkSubscriptionOwnerAsync("apple", verified.SubscriptionKey, ownerId);
        await store.UpsertSubscriptionAsync(
            new EntitlementRecord(
                ownerId,
                true,
                "apple",
                verified.ProductId,
                verified.ExpiresAt,
                DateTimeOffset.UtcNow,
                verified.SubscriptionKey));

        // Nothing is ever linked under the device's own transaction id.
        if (!string.Equals(ownTransactionId, originalTransactionId, StringComparison.Ordinal))
            Assert.Empty(await store.GetSubscriptionOwnersAsync("apple", ownTransactionId));

        return verified;
    }

    [Fact]
    public async Task DifferentTransactionIds_SameOriginalTransactionId_ShareOneSubscription()
    {
        var store = NewStore();
        var iPhoneA = EntitlementStore.NewAnonymousOwnerId();
        var iPhoneB = EntitlementStore.NewAnonymousOwnerId();

        // One subscription: Apple's stable originalTransactionId.
        const string original = "1000000555";

        // iPhone A — the original purchase. Its StoreKit transaction is 2000000001.
        await VerifyOnDeviceAsync(store, iPhoneA, "2000000001", original);

        // iPhone B — Restore Purchases. Apple reports its OWN transaction id
        // (2000000009) and the SAME originalTransactionId.
        await VerifyOnDeviceAsync(store, iPhoneB, "2000000009", original);

        // Exactly one subscription, with both installations on it.
        var owners = await store.GetSubscriptionOwnersAsync("apple", original);
        Assert.Equal(2, owners.Count);
        Assert.Contains(iPhoneA, owners);
        Assert.Contains(iPhoneB, owners);

        // The individual transaction ids own nothing: were the key taken from
        // them, iPhone B would have created a second "subscription" and the
        // renewal/refund fan-out below could not reach both devices.
        Assert.Empty(await store.GetSubscriptionOwnersAsync("apple", "2000000001"));
        Assert.Empty(await store.GetSubscriptionOwnersAsync("apple", "2000000009"));

        // Both devices get the full plan.
        var a = await store.GetAccessAsync(iPhoneA);
        var b = await store.GetAccessAsync(iPhoneB);
        Assert.True(a.HasObd && a.HasVin);
        Assert.True(b.HasObd && b.HasVin);
    }

    [Fact]
    public async Task ARenewalWithANewTransactionId_UpdatesEveryLinkedDevice()
    {
        var store = NewStore();
        var iPhoneA = EntitlementStore.NewAnonymousOwnerId();
        var iPhoneB = EntitlementStore.NewAnonymousOwnerId();

        const string original = "1000000777";
        var firstExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();

        await VerifyOnDeviceAsync(store, iPhoneA, "2000000101", original,
            DateTimeOffset.FromUnixTimeSeconds(firstExpiry));
        await VerifyOnDeviceAsync(store, iPhoneB, "2000000109", original,
            DateTimeOffset.FromUnixTimeSeconds(firstExpiry));

        // Renewal: a NEW transaction id (2000000117) for the same subscription.
        var renewed = DateTimeOffset.UtcNow.AddDays(60).ToUnixTimeSeconds();
        foreach (var owner in await store.GetSubscriptionOwnersAsync("apple", original))
        {
            await store.UpsertSubscriptionAsync(
                new EntitlementRecord(owner, true, "apple", ProductCatalog.FullMonthly,
                    DateTimeOffset.FromUnixTimeSeconds(renewed), DateTimeOffset.UtcNow, original));
        }

        var a = await store.GetAccessAsync(iPhoneA);
        var b = await store.GetAccessAsync(iPhoneB);
        Assert.True(a.HasObd && b.HasObd);
        Assert.Equal(renewed, a.ObdExpiresAt!.Value.ToUnixTimeSeconds());
        Assert.Equal(renewed, b.ObdExpiresAt!.Value.ToUnixTimeSeconds());

        // The renewal did not invent a second subscription keyed by its own
        // transaction id.
        Assert.Empty(await store.GetSubscriptionOwnersAsync("apple", "2000000117"));
        Assert.Equal(2, (await store.GetSubscriptionOwnersAsync("apple", original)).Count);
    }

    [Fact]
    public void AnAppleNotificationPayload_KeepsTheTransactionIdAndOriginalTransactionIdApart()
    {
        // App Store Server Notifications V2 carry signedTransactionInfo as a JWS;
        // only its payload is needed here (the signature is checked earlier, in
        // VerifyAppleJws). This pins that the handler reads the subscription's
        // stable originalTransactionId and not the notification's own transaction.
        const string original = "1000000888";
        const string transaction = "2000000999";

        var jwt = "a." + Base64Url(
            "{\"transactionId\":\"" + transaction + "\"," +
            "\"originalTransactionId\":\"" + original + "\"," +
            "\"productId\":\"" + ProductCatalog.FullMonthly + "\"}") + ".c";

        using var document = System.Text.Json.JsonDocument.Parse(
            "{\"data\":{\"signedTransactionInfo\":\"" + jwt + "\"}}");

        var info = StoreVerificationService.NotificationTransaction(document.RootElement);

        Assert.NotNull(info);
        Assert.Equal(transaction, info!.TransactionId);
        Assert.Equal(original, info.OriginalTransactionId);

        // Which is what the notification handler prefers when it re-reads Apple:
        // appleTx?.OriginalTransactionId ?? hinted.OriginalTransactionId.
        Assert.NotEqual(info.TransactionId, info.OriginalTransactionId);
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    [Fact]
    public async Task IPhoneA_PurchasesFullPro_AndIsActive()
    {
        var store = NewStore();
        var iPhoneA = EntitlementStore.NewAnonymousOwnerId();

        await LinkDeviceAsync(store, iPhoneA, "OTID-A", ProductCatalog.FullMonthly, null);

        var access = await store.GetAccessAsync(iPhoneA);
        Assert.True(access.HasObd);
        Assert.True(access.HasVin);
        Assert.Equal("full", access.Tier);
    }

    [Fact]
    public async Task IPhoneB_RestoresTheSameSubscription_AndBothDevicesKeepPro()
    {
        var store = NewStore();
        var iPhoneA = EntitlementStore.NewAnonymousOwnerId();
        var iPhoneB = EntitlementStore.NewAnonymousOwnerId();

        // iPhone A buys Full PRO without an account.
        await LinkDeviceAsync(store, iPhoneA, "OTID-SHARED", ProductCatalog.FullMonthly, null);

        // iPhone B restores the same Apple subscription (same Apple Account).
        await LinkDeviceAsync(store, iPhoneB, "OTID-SHARED", ProductCatalog.FullMonthly, null);

        var a = await store.GetAccessAsync(iPhoneA);
        var b = await store.GetAccessAsync(iPhoneB);

        Assert.True(a.HasObd && a.HasVin, "iPhone A must keep PRO after iPhone B restored");
        Assert.True(b.HasObd && b.HasVin, "iPhone B must get PRO from the restore");
        Assert.Equal("full", a.Tier);
        Assert.Equal("full", b.Tier);
    }

    [Fact]
    public async Task RestoringOnB_DoesNotRevokeA()
    {
        var store = NewStore();
        var iPhoneA = EntitlementStore.NewAnonymousOwnerId();
        var iPhoneB = EntitlementStore.NewAnonymousOwnerId();

        await LinkDeviceAsync(store, iPhoneA, "OTID-KEEP", ProductCatalog.FullMonthly, null);

        var before = await store.GetAccessAsync(iPhoneA);
        Assert.True(before.HasObd);

        await LinkDeviceAsync(store, iPhoneB, "OTID-KEEP", ProductCatalog.FullMonthly, null);

        // The entitlement iPhone A already had is untouched: same flags, same
        // expiry, still active.
        var after = await store.GetAccessAsync(iPhoneA);
        Assert.True(after.HasObd);
        Assert.True(after.HasVin);
        Assert.Equal(before.ObdExpiresAt, after.ObdExpiresAt);
        Assert.Equal(before.VinExpiresAt, after.VinExpiresAt);
    }

    [Fact]
    public async Task BothDevicesStayLinked_AndLinkingIsIdempotent()
    {
        var store = NewStore();
        var iPhoneA = EntitlementStore.NewAnonymousOwnerId();
        var iPhoneB = EntitlementStore.NewAnonymousOwnerId();

        await LinkDeviceAsync(store, iPhoneA, "OTID-LINK", ProductCatalog.FullMonthly, null);
        await LinkDeviceAsync(store, iPhoneB, "OTID-LINK", ProductCatalog.FullMonthly, null);

        // A second verify/refresh on the same device must not duplicate anything.
        await store.LinkSubscriptionOwnerAsync("apple", "OTID-LINK", iPhoneA);
        await store.LinkSubscriptionOwnerAsync("apple", "OTID-LINK", iPhoneA);

        var owners = await store.GetSubscriptionOwnersAsync("apple", "OTID-LINK");
        Assert.Equal(2, owners.Count);
        Assert.Contains(iPhoneA, owners);
        Assert.Contains(iPhoneB, owners);

        // Sharing a subscription never promotes it to account ownership: the
        // owner that can be found for it is an installation, not an account.
        Assert.True(EntitlementStore.IsAnonymousOwner(
            await store.FindClaimOwnerAsync("apple", "OTID-LINK")));
    }

    [Fact]
    public async Task Reinstalling_AllowsRestoration()
    {
        // A reinstall has lost its keychain token, so it is a brand-new owner:
        // it restores the same Apple subscription and gets access, while the
        // pre-reinstall installation is unaffected.
        var store = NewStore();
        var beforeReinstall = EntitlementStore.NewAnonymousOwnerId();
        var afterReinstall = EntitlementStore.NewAnonymousOwnerId();

        await LinkDeviceAsync(store, beforeReinstall, "OTID-REINSTALL", ProductCatalog.FullMonthly, null);
        await LinkDeviceAsync(store, afterReinstall, "OTID-REINSTALL", ProductCatalog.FullMonthly, null);

        Assert.True((await store.GetAccessAsync(afterReinstall)).HasObd);
        Assert.True((await store.GetAccessAsync(beforeReinstall)).HasObd);
        Assert.Equal(2, (await store.GetSubscriptionOwnersAsync("apple", "OTID-REINSTALL")).Count);
    }

    [Fact]
    public async Task AnExpiredSubscription_GrantsNothingOnAnyLinkedDevice()
    {
        var store = NewStore();
        var iPhoneA = EntitlementStore.NewAnonymousOwnerId();
        var iPhoneB = EntitlementStore.NewAnonymousOwnerId();
        var expired = DateTimeOffset.UtcNow.AddMinutes(-1);

        await store.LinkSubscriptionOwnerAsync("apple", "OTID-EXPIRED", iPhoneA);
        await store.UpsertSubscriptionAsync(
            new EntitlementRecord(iPhoneA, true, "apple", ProductCatalog.FullMonthly, expired, DateTimeOffset.UtcNow, "OTID-EXPIRED"));

        await store.LinkSubscriptionOwnerAsync("apple", "OTID-EXPIRED", iPhoneB);
        await store.UpsertSubscriptionAsync(
            new EntitlementRecord(iPhoneB, true, "apple", ProductCatalog.FullMonthly, expired, DateTimeOffset.UtcNow, "OTID-EXPIRED"));

        Assert.False((await store.GetAccessAsync(iPhoneA)).HasObd);
        Assert.False((await store.GetAccessAsync(iPhoneB)).HasObd);
    }

    [Fact]
    public async Task ARefundedSubscription_IsRevokedOnEveryLinkedDevice()
    {
        // What the Apple REFUND notification does: revoke the subscription for
        // every installation linked to that subscription key. The two devices
        // present different transaction ids but share one originalTransactionId.
        var store = NewStore();
        var iPhoneA = EntitlementStore.NewAnonymousOwnerId();
        var iPhoneB = EntitlementStore.NewAnonymousOwnerId();

        const string original = "1000000911";
        await VerifyOnDeviceAsync(store, iPhoneA, "2000000901", original);
        await VerifyOnDeviceAsync(store, iPhoneB, "2000000909", original);

        foreach (var owner in await store.GetSubscriptionOwnersAsync("apple", original))
        {
            Assert.True(await store.RevokeSubscriptionAsync(owner, "apple", original));
        }

        Assert.False((await store.GetAccessAsync(iPhoneA)).HasObd);
        Assert.False((await store.GetAccessAsync(iPhoneB)).HasObd);
        Assert.False((await store.GetAccessAsync(iPhoneA)).HasVin);
        Assert.False((await store.GetAccessAsync(iPhoneB)).HasVin);
    }

    [Fact]
    public async Task AnUnrelatedAccount_CannotTakeOverTheSubscription()
    {
        var store = NewStore();
        var iPhoneA = EntitlementStore.NewAnonymousOwnerId();
        var unrelatedAccount = Guid.NewGuid().ToString("N");

        await LinkDeviceAsync(store, iPhoneA, "OTID-OWN", ProductCatalog.FullMonthly, null);

        // While the subscription is accountless, whoever OWNS it is an
        // installation — never an OBD360 account. That is what lets a second
        // device of the same Apple Account link to it, and what makes the
        // endpoint answer LINKED_TO_ACCOUNT (not a link) for account-owned ones.
        var owner = await store.FindClaimOwnerAsync("apple", "OTID-OWN");
        Assert.NotNull(owner);
        Assert.True(EntitlementStore.IsAnonymousOwner(owner));

        // Once an account legitimately claims it, a *different* account cannot.
        Assert.True(await store.TryClaimTransactionAsync("apple", "OTID-OWN", unrelatedAccount, false));
        Assert.False(await store.TryClaimTransactionAsync("apple", "OTID-OWN", Guid.NewGuid().ToString("N"), false));
        Assert.Equal(unrelatedAccount, await store.FindClaimOwnerAsync("apple", "OTID-OWN"));
    }

    [Fact]
    public async Task AnAccountOwnedTransaction_IsNeverHeldByAnAccountlessDevice()
    {
        // The verify endpoint refuses with LINKED_TO_ACCOUNT when the claim
        // belongs to an account, so an installation can never be the owner of
        // somebody else's account purchase.
        var store = NewStore();
        var account = Guid.NewGuid().ToString("N");

        Assert.True(await store.TryClaimTransactionAsync("apple", "OTID-ACCOUNT", account, false));

        var owner = await store.FindClaimOwnerAsync("apple", "OTID-ACCOUNT");
        Assert.Equal(account, owner);
        Assert.False(EntitlementStore.IsAnonymousOwner(owner));
    }

    // ============================================================
    // INDEPENDENT SESSIONS
    // ============================================================

    [Fact]
    public async Task EachInstallation_KeepsItsOwnIndependentSession()
    {
        var store = NewStore();

        var tokenA = await store.CreateAnonymousSessionAsync(EntitlementStore.NewAnonymousOwnerId(), "install-a");
        var tokenB = await store.CreateAnonymousSessionAsync(EntitlementStore.NewAnonymousOwnerId(), "install-b");

        var ownerA = await store.ResolveAnonymousOwnerAsync(tokenA);
        var ownerB = await store.ResolveAnonymousOwnerAsync(tokenB);

        Assert.NotNull(ownerA);
        Assert.NotNull(ownerB);
        Assert.NotEqual(ownerA, ownerB);

        // Resolving one session must not disturb the other, and vice versa.
        Assert.Equal(ownerA, await store.ResolveAnonymousOwnerAsync(tokenA));
        Assert.Equal(ownerB, await store.ResolveAnonymousOwnerAsync(tokenB));
    }

    // ============================================================
    // ENTITLEMENT MAPPING (the three plans)
    // ============================================================

    [Theory]
    [InlineData(ProductCatalog.ObdMonthly, true, false)]
    [InlineData(ProductCatalog.VinMonthly, false, true)]
    [InlineData(ProductCatalog.FullMonthly, true, true)]
    public void EachPlan_GrantsExactlyItsOwnFeatures(string productId, bool obd, bool vin)
    {
        Assert.Equal(obd, ProductCatalog.GrantsObd(productId, "apple"));
        Assert.Equal(vin, ProductCatalog.GrantsVin(productId, "apple"));
    }

    [Fact]
    public void FullPro_IsTheOnlySinglePlanThatUnlocksBoth()
    {
        Assert.True(ProductCatalog.GrantsObd(ProductCatalog.FullMonthly, "apple") &&
                    ProductCatalog.GrantsVin(ProductCatalog.FullMonthly, "apple"));

        // OBD PRO must never unlock VIN PRO features, and the other way round.
        Assert.False(ProductCatalog.GrantsVin(ProductCatalog.ObdMonthly, "apple"));
        Assert.False(ProductCatalog.GrantsObd(ProductCatalog.VinMonthly, "apple"));
    }

    [Fact]
    public async Task AVinProSubscription_UnlocksVinButNotObd()
    {
        var store = NewStore();
        var owner = EntitlementStore.NewAnonymousOwnerId();

        await store.UpsertSubscriptionAsync(
            new EntitlementRecord(owner, true, "apple", ProductCatalog.VinMonthly, DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow, "OTID-3"));

        var access = await store.GetAccessAsync(owner);

        Assert.False(access.HasObd);
        Assert.True(access.HasVin);
        Assert.Equal("vin", access.Tier);
    }

    [Fact]
    public async Task AnExpiredSubscription_GrantsNothing()
    {
        var store = NewStore();
        var owner = EntitlementStore.NewAnonymousOwnerId();

        await store.UpsertSubscriptionAsync(
            new EntitlementRecord(owner, true, "apple", ProductCatalog.FullMonthly, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, "OTID-4"));

        var access = await store.GetAccessAsync(owner);

        Assert.False(access.HasObd);
        Assert.False(access.HasVin);
        Assert.Equal("none", access.Tier);
    }

    [Fact]
    public async Task AnAccountEntitlementAndAnAppleEntitlement_DoNotOverwriteEachOther()
    {
        var store = NewStore();
        var accountId = Guid.NewGuid().ToString("N");
        var owner = EntitlementStore.NewAnonymousOwnerId();

        // Website purchase on the account...
        await store.UpsertSubscriptionAsync(
            new EntitlementRecord(accountId, true, "web", ProductCatalog.VinMonthly, DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow, "web:order-1"));

        // ...and an accountless App Store purchase of the other plan.
        await store.UpsertSubscriptionAsync(
            new EntitlementRecord(owner, true, "apple", ProductCatalog.ObdMonthly, DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow, "OTID-5"));

        var account = await store.GetAccessAsync(accountId);
        var apple = await store.GetAccessAsync(owner);

        Assert.True(account.HasVin);
        Assert.False(account.HasObd);
        Assert.True(apple.HasObd);
        Assert.False(apple.HasVin);
    }
}
