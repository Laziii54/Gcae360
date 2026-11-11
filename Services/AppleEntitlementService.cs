using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CarScanner.Services;

/// <summary>
/// An entitlement the backend derived from a verified Apple transaction.
/// <see cref="Verified"/> is false only when Apple could not be reached while
/// refreshing — the last known server-verified state is then returned instead
/// of revoking a paying customer on a network error.
/// </summary>
public sealed record AppleEntitlement(
    bool HasObd,
    bool HasVin,
    string Tier,
    DateTimeOffset? ObdExpiresAt,
    DateTimeOffset? VinExpiresAt,
    string? ProductId,
    bool Verified);

/// <summary>
/// Result of sending one StoreKit transaction to the backend without an
/// OBD360 account. <see cref="Reached"/> distinguishes "the server said no"
/// from "the request never got there", so the UI can tell a network problem
/// apart from a rejected/expired purchase.
/// </summary>
public sealed record ApplePurchaseVerification(
    bool Reached,
    AppleEntitlement? Entitlement,
    string? ErrorCode)
{
    public static readonly ApplePurchaseVerification Unreachable = new(false, null, null);

    public static ApplePurchaseVerification Failed(string code) => new(true, null, code);
}

/// <summary>
/// App Store purchases that belong to no OBD360 account.
///
/// App Review rejected build 56 under Guideline 5.1.1(v) because a subscription
/// could not be bought without registering. Apple already knows who the
/// customer is, so an in-app purchase must not need an OBD360 account:
/// StoreKit completes the transaction, the backend verifies it with the App
/// Store Server API, and the entitlement is bound to an opaque, server-issued
/// session token that this device keeps in the keychain. No e-mail, no
/// password, no account row.
///
/// The token is only a handle: the entitlement itself always comes from the
/// backend, which never trusts a client claim. Uninstalling the app (or moving
/// to another device) is recovered by StoreKit's Restore Purchases, which
/// re-verifies the same Apple subscription and re-binds it to the new
/// installation.
/// </summary>
public sealed class AppleEntitlementService
{
    /// <summary>Keychain key of the accountless session token (never a password or a secret of Apple's).</summary>
    private const string TokenKey = "apple_store_session_token";

    private readonly AccountService _account;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public AppleEntitlementService(AccountService account)
    {
        _account = account;
    }

    /// <summary>Accountless StoreKit purchases exist on iOS only (the Android app sells nothing in-app).</summary>
    public static bool IsSupported =>
        DeviceInfo.Current.Platform == DevicePlatform.iOS;

    /// <summary>True when this installation already holds an accountless session.</summary>
    public async Task<bool> HasSessionAsync()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(
                await SecureStorage.Default.GetAsync(TokenKey));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends one finished StoreKit transaction to the backend without any
    /// OBD360 account.
    /// </summary>
    public async Task<ApplePurchaseVerification> VerifyAsync(
        string productId,
        string transactionId,
        string? originalTransactionId,
        CancellationToken token = default)
    {
        var (reached, dto) = await PostAsync(
            "/api/store/apple/anonymous/verify",
            new
            {
                productId,
                transactionId,
                originalTransactionId,
                token = await ReadTokenAsync(),
                installId = _account.EffectiveUserId
            },
            token);

        if (!reached)
            return ApplePurchaseVerification.Unreachable;

        if (dto is null)
            return ApplePurchaseVerification.Failed("INVALID_RESPONSE");

        if (dto.Success == true)
            return new ApplePurchaseVerification(true, dto.ToEntitlement(), null);

        return ApplePurchaseVerification.Failed(dto.Code ?? "NOT_VERIFIED");
    }

    /// <summary>Asks the backend to re-read the subscription state from Apple (renewal, expiry, revocation).</summary>
    public async Task<AppleEntitlement?> RefreshAsync(CancellationToken token = default)
    {
        if (!await HasSessionAsync())
            return null;

        var (reached, dto) = await PostAsync("/api/store/apple/anonymous/refresh", new { }, token);
        if (!reached || dto?.Success != true)
            return null;

        return dto.ToEntitlement();
    }

    /// <summary>The current server-side entitlement of this installation, without asking Apple.</summary>
    public async Task<AppleEntitlement?> ReadAsync(CancellationToken token = default)
    {
        if (!await HasSessionAsync())
            return null;

        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(
                HttpMethod.Get,
                "/api/store/apple/anonymous");

            var stored = await ReadTokenAsync();
            if (string.IsNullOrWhiteSpace(stored))
                return null;
            request.Headers.TryAddWithoutValidation("X-GCS-Anon-Token", stored);

            using var response = await _http.SendAsync(request, token);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await ForgetAsync();
                return null;
            }

            if (!response.IsSuccessStatusCode)
                return null;

            var dto = await response.Content.ReadFromJsonAsync<AppleEntitlementDto>(
                cancellationToken: token);

            return dto?.Success == true ? dto.ToEntitlement() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Drops the local session handle (the purchase itself stays with the Apple Account).</summary>
    public async Task ForgetAsync()
    {
        try { SecureStorage.Default.Remove(TokenKey); }
        catch { }
        await Task.CompletedTask;
    }

    private async Task<(bool Reached, AppleEntitlementDto? Dto)> PostAsync(
        string path,
        object body,
        CancellationToken token)
    {
        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Post, path);

            var stored = await ReadTokenAsync();
            if (!string.IsNullOrWhiteSpace(stored))
                request.Headers.TryAddWithoutValidation("X-GCS-Anon-Token", stored);

            request.Content = JsonContent.Create(body);

            using var response = await _http.SendAsync(request, token);
            var dto = await response.Content.ReadFromJsonAsync<AppleEntitlementDto>(
                cancellationToken: token);

            // The token is issued once, when the backend creates the session.
            if (!string.IsNullOrWhiteSpace(dto?.Token))
                await WriteTokenAsync(dto!.Token!);

            // An unknown/expired session is dropped so the next purchase or
            // restore starts a fresh, verified one.
            if (dto is not null &&
                string.Equals(dto.Code, "UNKNOWN_SESSION", StringComparison.Ordinal))
            {
                await ForgetAsync();
            }

            return (true, dto);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (false, null);
        }
    }

    private static async Task<string?> ReadTokenAsync()
    {
        try { return await SecureStorage.Default.GetAsync(TokenKey); }
        catch { return null; }
    }

    private static async Task WriteTokenAsync(string token)
    {
        try { await SecureStorage.Default.SetAsync(TokenKey, token); }
        catch { }
    }

    private sealed class AppleEntitlementDto
    {
        [JsonPropertyName("success")] public bool? Success { get; set; }
        [JsonPropertyName("hasObd")] public bool? HasObd { get; set; }
        [JsonPropertyName("hasVin")] public bool? HasVin { get; set; }
        [JsonPropertyName("tier")] public string? Tier { get; set; }
        [JsonPropertyName("obdExpiresAt")] public DateTimeOffset? ObdExpiresAt { get; set; }
        [JsonPropertyName("vinExpiresAt")] public DateTimeOffset? VinExpiresAt { get; set; }
        [JsonPropertyName("productId")] public string? ProductId { get; set; }
        [JsonPropertyName("token")] public string? Token { get; set; }
        [JsonPropertyName("verified")] public bool? Verified { get; set; }
        [JsonPropertyName("code")] public string? Code { get; set; }

        public AppleEntitlement ToEntitlement() => new(
            HasObd == true,
            HasVin == true,
            Tier ?? "none",
            ObdExpiresAt,
            VinExpiresAt,
            ProductId,
            Verified != false);
    }
}
