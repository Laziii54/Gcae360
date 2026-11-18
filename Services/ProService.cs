using CarScanner.Database;
using CarScanner.Models;
using CarScanner.Services.Entitlements;
using System.Net.Http.Json;

namespace CarScanner.Services;

/// <summary>
/// The one place the app decides whether a PRO feature is unlocked.
///
/// Access is granted by a <b>cryptographically signed entitlement token</b>
/// issued by the OBD360 backend and verified locally by
/// <see cref="EntitlementTokenVerifier"/>. Local booleans (the old
/// <c>IsPro</c> / <c>VerifiedByServer</c> columns, or a preference file) are no
/// longer evidence of anything: without a token signed by the backend's private
/// key, PRO stays locked — so editing the SQLite database or the preferences on
/// a rooted device no longer unlocks anything.
///
/// Two independent sources are combined, each with its own token:
///  • the OBD360 account (website purchase, account-linked store purchase,
///    admin/promo plan) — refreshed through <c>/api/entitlements/{id}</c>;
///  • an accountless App Store subscription bought on iOS — refreshed through
///    <c>/api/store/apple/anonymous/refresh</c>.
/// Signing out drops only the account token, so an Apple subscription keeps
/// working (App Review 5.1.1(v)).
///
/// Tokens are short-lived (7 days by default) and verified on every load, so an
/// offline device keeps working only until the token it holds expires.
/// </summary>
public sealed class ProService
{
    private const string AccountTokenKey = "entitlement.token.account";
    private const string AppleTokenKey = "entitlement.token.apple";

    private readonly AppDatabase _db;
    private readonly AccountService _account;
    private readonly AppleEntitlementService _appleStore;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DateTimeOffset _lastRefreshAttempt = DateTimeOffset.MinValue;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>Refresh proactively once a token is this close to expiring.</summary>
    private static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromDays(1);

    private EntitlementTokenClaims? _accountClaims;
    private EntitlementTokenClaims? _appleClaims;
    private bool _migrationStarted;

    // ---- Subscriptions: monthly, the only period the mobile apps sell ----
    public const string ObdProductId = "com.georgiancarscanner.pro.monthly";
    public const string VinProductId = "com.georgiancarscanner.vin.monthly";
    public const string FullProductId = "com.georgiancarscanner.full.monthly";
    public const string ProductId = ObdProductId;

    // ---- Yearly ids: website only. Never requested from StoreKit by the app. ----
    // They stay here because a website yearly purchase is still a real
    // entitlement (GrantsObd/GrantsVin on the backend), so the app must keep
    // understanding it; it just cannot be bought inside the mobile apps.
    public const string ObdYearlyProductId = "com.georgiancarscanner.pro.yearly";
    public const string VinYearlyProductId = "com.georgiancarscanner.vin.yearly";
    public const string FullYearlyProductId = "com.georgiancarscanner.full.yearly";

    // ---- One-time VIN credits ----
    public const string VinPack5ProductId = "com.georgiancarscanner.vin.pack5";
    public const string VinPack10ProductId = "com.georgiancarscanner.vin.pack10";
    public const string VinSingleProductId = "com.georgiancarscanner.vin.single";

    public const int VinMonthlyReports = 10;

    /// <summary>OBD PRO is granted by at least one verified token that says so.</summary>
    public bool IsPro => _accountClaims?.HasObd == true || _appleClaims?.HasObd == true;

    /// <summary>VIN PRO is granted by at least one verified token that says so.</summary>
    public bool HasVin => _accountClaims?.HasVin == true || _appleClaims?.HasVin == true;

    /// <summary>
    /// True when at least one token verified. Kept for compatibility with the
    /// old model; it can never be true without a valid signature.
    /// </summary>
    public bool ServerVerified => _accountClaims is not null || _appleClaims is not null;

    /// <summary>OBD PRO check used by every OBD PRO feature.</summary>
    public bool HasObd => IsPro && ServerVerified;

    public DateTime? ExpiresAt => LatestExpiry(claims => claims.HasObd);

    public DateTime? VinExpiresAt => LatestExpiry(claims => claims.HasVin);

    /// <summary>True when the current access comes from an accountless App Store subscription.</summary>
    public bool IsAppleEntitled => _appleClaims is not null;

    /// <summary>True when an OBD360 account token is present and valid.</summary>
    public bool IsAccountEntitled => _accountClaims is not null;

    /// <summary>none, obd, vin or full.</summary>
    public string Tier => HasObd && HasVin ? "full" : HasObd ? "obd" : HasVin ? "vin" : "none";

    /// <summary>
    /// True when a token exists but has expired and no refresh has succeeded —
    /// the UI uses this to say "reconnect to verify PRO" instead of "buy PRO".
    /// </summary>
    public bool NeedsReconnection { get; private set; }

    public event EventHandler<bool>? ProChanged;

    public ProService(AppDatabase db, AccountService account, AppleEntitlementService appleStore)
    {
        _db = db;
        _account = account;
        _appleStore = appleStore;
        _account.AccountChanged += OnAccountChanged;
    }

    // ============================================================
    // LIFECYCLE
    // ============================================================

    /// <summary>
    /// Signs out: drops the account token only. A verified App Store
    /// entitlement belongs to the Apple Account and must survive (5.1.1(v)).
    /// Signing in again invalidates the throttle so the new token is fetched.
    /// </summary>
    private async void OnAccountChanged(object? sender, EventArgs e)
    {
        try
        {
            if (!_account.IsSignedIn)
            {
                await ClearAccountEntitlementAsync();
                return;
            }

            _lastRefreshAttempt = DateTimeOffset.MinValue;
            await LoadAsync(forceServerRefresh: true);
        }
        catch
        {
            // Never let an account event crash the app.
        }
    }

    public async Task LoadAsync(bool forceServerRefresh = false)
    {
        StartLegacyMigration();
        await LoadTokensAsync();

        if (!forceServerRefresh &&
            DateTimeOffset.UtcNow - _lastRefreshAttempt < RefreshInterval)
        {
            Publish();
            return;
        }

        await RefreshAllAsync(forceServerRefresh);
        Publish();
    }

    /// <summary>OBD PRO check used by every OBD PRO feature.</summary>
    public async Task<bool> EnsureProAsync()
    {
        await LoadAsync();
        return HasObd;
    }

    /// <summary>VIN PRO check (the server still counts every report).</summary>
    public async Task<bool> EnsureVinAsync()
    {
        await LoadAsync();
        return HasVin;
    }

    public bool CanUse(string feature) => HasObd;

    /// <summary>Re-reads tokens from storage and, when forced, refreshes them from the backend.</summary>
    public async Task RefreshAsync(bool forceServerRefresh = true)
    {
        StartLegacyMigration();
        await LoadTokensAsync();
        await RefreshAllAsync(forceServerRefresh);
        Publish();
    }

    /// <summary>
    /// Applies the signed token returned by the accountless Apple purchase
    /// flow, so the UI updates without a second round trip.
    /// </summary>
    public async Task ApplyAppleEntitlementAsync(AppleEntitlement entitlement)
    {
        var claims = EntitlementTokenVerifier.Verify(entitlement.EntitlementToken);
        if (claims is null)
        {
            // No usable token in the answer: grant nothing. The next refresh
            // will try again; PRO is never inferred from the response flags.
            return;
        }

        await StoreTokenAsync(AppleTokenKey, entitlement.EntitlementToken!, claims, isApple: true);
        Publish();
    }

    /// <summary>Clears every token (account deletion / full reset).</summary>
    public async Task DeactivateAsync()
    {
        _accountClaims = null;
        _appleClaims = null;
        await RemoveTokenAsync(AccountTokenKey);
        await RemoveTokenAsync(AppleTokenKey);
        await _appleStore.ForgetAsync();

        // Leave a clean, non-authoritative record for the data export and remove
        // every legacy value that the old build trusted.
        try
        {
            await _db.SaveProAsync(new ProEntitlement { Id = "local", IsPro = false, VerifiedByServer = false, UpdatedAt = DateTime.UtcNow });
        }
        catch
        {
        }

        Publish();
    }

    private async Task ClearAccountEntitlementAsync()
    {
        _accountClaims = null;
        await RemoveTokenAsync(AccountTokenKey);
        Publish();
    }

    // ============================================================
    // TOKEN STORAGE / VALIDATION
    // ============================================================

    private async Task LoadTokensAsync()
    {
        var account = await ReadVerifiedAsync(AccountTokenKey);
        var apple = await ReadVerifiedAsync(AppleTokenKey);

        _accountClaims = account.Claims;
        _appleClaims = apple.Claims;

        // A token that existed but no longer verifies (expired, or signed by a
        // retired key) is what "reconnect to verify PRO" is about. A user who
        // never had PRO sees the normal purchase screen instead.
        NeedsReconnection = (account.Lost || apple.Lost) &&
                            _accountClaims is null &&
                            _appleClaims is null;
    }

    private static async Task<(EntitlementTokenClaims? Claims, bool Lost)> ReadVerifiedAsync(string key)
    {
        try
        {
            var stored = await SecureStorage.Default.GetAsync(key);
            if (string.IsNullOrWhiteSpace(stored))
                return (null, false);

            var claims = EntitlementTokenVerifier.Verify(stored);
            if (claims is null)
            {
                // Corrupt, tampered or expired: drop it rather than keep a dead token.
                SecureStorage.Default.Remove(key);
                return (null, true);
            }

            return (claims, false);
        }
        catch
        {
            // Secure storage unavailable (locked keystore, restored backup, ...):
            // behave as "no entitlement" rather than guessing.
            return (null, false);
        }
    }

    private async Task StoreTokenAsync(string key, string token, EntitlementTokenClaims claims, bool isApple)
    {
        try
        {
            await SecureStorage.Default.SetAsync(key, token);
        }
        catch
        {
            // Storage failure must not unlock anything by accident; the claims are
            // still kept in memory for this session only.
        }

        if (isApple)
            _appleClaims = claims;
        else
            _accountClaims = claims;
    }

    private static async Task RemoveTokenAsync(string key)
    {
        try
        {
            SecureStorage.Default.Remove(key);
            await Task.CompletedTask;
        }
        catch
        {
        }
    }

    // ============================================================
    // REFRESH
    // ============================================================

    private async Task RefreshAllAsync(bool force)
    {
        await _refreshGate.WaitAsync();
        try
        {
            if (!force &&
                DateTimeOffset.UtcNow - _lastRefreshAttempt < RefreshInterval)
            {
                return;
            }

            _lastRefreshAttempt = DateTimeOffset.UtcNow;

            if (_account.IsSignedIn)
                await RefreshAccountTokenAsync();

            if (AppleEntitlementService.IsSupported)
                await RefreshAppleTokenAsync();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Fetches a fresh token for the signed-in account. An account with no
    /// entitlement still gets a valid token — one that says hasObd/hasVin false —
    /// so a lapsed or revoked subscription is reflected immediately.
    /// </summary>
    private async Task RefreshAccountTokenAsync()
    {
        var userId = _account.AccountId;
        if (!Guid.TryParse(userId, out _))
            return;

        if (!Uri.TryCreate(AIConfig.BackendBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(
                HttpMethod.Get,
                $"/api/entitlements/{Uri.EscapeDataString(userId)}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return;

            var payload = await response.Content.ReadFromJsonAsync<EntitlementResponse>();
            if (string.IsNullOrWhiteSpace(payload?.EntitlementToken))
            {
                // The backend could not sign (no key configured). Keep whatever
                // token we already hold until it expires; never grant from flags.
                return;
            }

            var claims = EntitlementTokenVerifier.Verify(payload!.EntitlementToken);
            if (claims is null || !string.Equals(claims.Subject, userId, StringComparison.OrdinalIgnoreCase))
            {
                // A token for a different subject is never accepted for this account.
                return;
            }

            await StoreTokenAsync(AccountTokenKey, payload.EntitlementToken!, claims, isApple: false);
        }
        catch
        {
            // Offline or server error: the previously verified token (if still
            // unexpired) remains the only source of access.
        }
    }

    private async Task RefreshAppleTokenAsync()
    {
        var entitlement = await _appleStore.RefreshAsync();
        if (entitlement?.EntitlementToken is not { Length: > 0 } token)
            return;

        var claims = EntitlementTokenVerifier.Verify(token);
        if (claims is null)
            return;

        await StoreTokenAsync(AppleTokenKey, token, claims, isApple: true);
    }

    // ============================================================
    // MIGRATION
    // ============================================================

    /// <summary>
    /// One-time cleanup: the previous build trusted IsPro / VerifiedByServer
    /// straight out of SQLite. Those columns still exist (other code reads the
    /// row for the data export), but every trusted flag is zeroed so nothing can
    /// resurrect them, and the app now ignores them for authorization entirely.
    /// </summary>
    private void StartLegacyMigration()
    {
        if (_migrationStarted)
            return;

        _migrationStarted = true;
        _ = MigrateLegacyTrustAsync();
    }

    private async Task MigrateLegacyTrustAsync()
    {
        try
        {
            // The old VIN flag lived in Preferences and was trusted as PRO.
            // Only a signed token counts now.
            Preferences.Default.Remove("pro.hasVin");
            Preferences.Default.Remove("pro.vinExpires");
        }
        catch
        {
        }

        try
        {
            await _db.ClearLegacyEntitlementTrustAsync();
        }
        catch
        {
        }
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private List<EntitlementTokenClaims> Granted(Func<EntitlementTokenClaims, bool> predicate)
    {
        var result = new List<EntitlementTokenClaims>(2);
        if (_accountClaims is not null && predicate(_accountClaims))
            result.Add(_accountClaims);
        if (_appleClaims is not null && predicate(_appleClaims))
            result.Add(_appleClaims);
        return result;
    }

    private DateTime? LatestExpiry(Func<EntitlementTokenClaims, bool> predicate)
    {
        var grants = Granted(predicate);
        return grants.Count == 0
            ? null
            : grants.Max(claims => claims.ExpiresAt).UtcDateTime;
    }

    private void Publish() => ProChanged?.Invoke(this, HasObd);

    private sealed class EntitlementResponse
    {
        public bool IsPro { get; set; }
        public string? Source { get; set; }
        public string? ProductId { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool? HasObd { get; set; }
        public bool? HasVin { get; set; }
        public string? Tier { get; set; }
        public DateTimeOffset? ObdExpiresAt { get; set; }
        public DateTimeOffset? VinExpiresAt { get; set; }
        public string? EntitlementToken { get; set; }
    }
}
