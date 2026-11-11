using CarScanner.Database;
using CarScanner.Models;
using System.Net.Http.Json;

namespace CarScanner.Services;

/// <summary>
/// The one place the app decides whether a PRO feature is unlocked.
///
/// Two independent sources are combined, never overwritten:
///
///  1. <b>OBD360 account</b> — a website purchase, an account-linked store
///     purchase, or a plan given by an admin. Read from
///     <c>/api/entitlements/{accountId}</c> and only while signed in.
///
///  2. <b>Accountless App Store purchase</b> — an iOS subscription bought
///     without any OBD360 account (App Review 5.1.1(v)). Read from
///     <c>/api/store/apple/anonymous</c> with the opaque session token in the
///     keychain; the backend derived it from an Apple transaction.
///
/// Signing out clears only source 1, so an Apple subscription bought without
/// an account keeps working (and the reverse: an Apple subscription does not
/// make a signed-in account's website plan disappear). Nothing is ever granted
/// from a local flag alone: a cached value is trusted only while it is
/// unexpired *and* was written from a server-verified answer.
/// </summary>
public sealed class ProService
{
    private readonly AppDatabase _db;
    private readonly AccountService _account;
    private readonly AppleEntitlementService _appleStore;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DateTimeOffset _lastRefreshAttempt = DateTimeOffset.MinValue;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

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

    private const string AccountVinKey = "pro.hasVin";
    private const string AccountVinExpiresKey = "pro.vinExpires";

    // ---- Account source (never includes the Apple source) ----
    private bool _accountObd;
    private DateTime? _accountObdExpiresAt;
    private bool _accountVin;
    private DateTime? _accountVinExpiresAt;
    private string _accountSource = "none";
    private string _accountProductId = string.Empty;

    // ---- Accountless App Store source ----
    private bool _appleObd;
    private bool _appleVin;
    private DateTime? _appleObdExpiresAt;
    private DateTime? _appleVinExpiresAt;
    private string _appleProductId = string.Empty;

    private bool _accountVerified;
    private bool _appleVerified;

    /// <summary>OBD PRO is active from either source.</summary>
    public bool IsPro => _accountObd || _appleObd;

    /// <summary>Latest end date of the OBD PRO sources that are active.</summary>
    public DateTime? ExpiresAt => Latest(
        _accountObd ? _accountObdExpiresAt : null,
        _appleObd ? _appleObdExpiresAt : null);

    /// <summary>True when at least one source was confirmed by the backend.</summary>
    public bool ServerVerified => _accountVerified || _appleVerified;

    /// <summary>VIN PRO is active from either source.</summary>
    public bool HasVin => _accountVin || _appleVin;

    public DateTime? VinExpiresAt => Latest(
        _accountVin ? _accountVinExpiresAt : null,
        _appleVin ? _appleVinExpiresAt : null);

    /// <summary>OBD PRO check used by every OBD PRO feature.</summary>
    public bool HasObd => IsPro && ServerVerified;

    /// <summary>True when the current access comes from an accountless App Store subscription.</summary>
    public bool IsAppleEntitled => _appleObd || _appleVin;

    public string? AppleProductId => string.IsNullOrEmpty(_appleProductId) ? null : _appleProductId;

    /// <summary>none, obd, vin or full.</summary>
    public string Tier => HasObd && HasVin ? "full" : HasObd ? "obd" : HasVin ? "vin" : "none";

    public event EventHandler<bool>? ProChanged;

    public ProService(AppDatabase db, AccountService account, AppleEntitlementService appleStore)
    {
        _db = db;
        _account = account;
        _appleStore = appleStore;
        _account.AccountChanged += OnAccountChanged;
    }

    /// <summary>
    /// PRO is per-account, never implied by "no session": signing out must clear
    /// the account's cached entitlement, because the refresh attempted right
    /// after logout has no access token any more, fails silently, and the
    /// failure path deliberately preserves the cache to survive real outages.
    ///
    /// It must <b>not</b> clear an accountless App Store entitlement: that
    /// purchase was verified against the Apple Account, not against OBD360, and
    /// App Review requires it to keep working after a sign-out.
    ///
    /// AccountChanged fires only after AccountService has already cleared the
    /// stored session, so IsSignedIn is reliable here.
    /// </summary>
    private async void OnAccountChanged(object? sender, EventArgs e)
    {
        if (_account.IsSignedIn)
            return;

        try { await ClearAccountEntitlementAsync(); } catch { }
    }

    public async Task LoadAsync(bool forceServerRefresh = false)
    {
        await LoadFromCacheAsync();

        if (!forceServerRefresh &&
            DateTimeOffset.UtcNow - _lastRefreshAttempt < RefreshInterval)
        {
            return;
        }

        await RefreshAllAsync(forceServerRefresh);
    }

    /// <summary>OBD PRO check used by every OBD PRO feature.</summary>
    public async Task<bool> EnsureProAsync()
    {
        await LoadAsync();
        return IsPro && ServerVerified;
    }

    /// <summary>VIN PRO check (the server still counts every report).</summary>
    public async Task<bool> EnsureVinAsync()
    {
        await LoadAsync();
        return HasVin;
    }

    public bool CanUse(string feature) => IsPro && ServerVerified;

    /// <summary>
    /// A verified Apple purchase, handed over by <see cref="PurchaseService"/>
    /// right after the backend accepted it, so the UI does not need a second
    /// round trip to show PRO.
    /// </summary>
    public async Task ApplyAppleEntitlementAsync(AppleEntitlement entitlement)
    {
        ApplyApple(entitlement);
        await SaveCacheAsync();
        Publish();
    }

    /// <summary>Re-reads both sources, bypassing the throttle.</summary>
    public async Task RefreshAsync(bool forceServerRefresh = true)
    {
        await LoadFromCacheAsync();
        await RefreshAllAsync(forceServerRefresh);
    }

    // ============================================================
    // ACCOUNT SOURCE
    // ============================================================

    /// <summary>Clears every cached entitlement (account *and* Apple) — a full reset.</summary>
    public async Task DeactivateAsync()
    {
        _accountObd = false;
        _accountObdExpiresAt = null;
        _accountSource = "none";
        _accountProductId = string.Empty;
        _accountVerified = false;
        SaveAccountVin(false, null);

        _appleObd = false;
        _appleVin = false;
        _appleObdExpiresAt = null;
        _appleVinExpiresAt = null;
        _appleProductId = string.Empty;
        _appleVerified = false;
        await _appleStore.ForgetAsync();

        await _db.SaveProAsync(new ProEntitlement { Id = "local", IsPro = false, VerifiedByServer = false, UpdatedAt = DateTime.UtcNow });
        Publish();
    }

    /// <summary>Sign-out: drop the account's entitlement, keep a verified Apple one.</summary>
    private async Task ClearAccountEntitlementAsync()
    {
        _accountObd = false;
        _accountObdExpiresAt = null;
        _accountSource = "none";
        _accountProductId = string.Empty;
        _accountVerified = false;
        SaveAccountVin(false, null);

        await SaveCacheAsync();
        Publish();
    }

    private async Task LoadFromCacheAsync()
    {
        var local = await _db.GetProAsync();
        var now = DateTime.UtcNow;

        _accountObd = local?.IsPro == true &&
                      local.VerifiedByServer &&
                      local.ExpiresAt is { } accountExpiry &&
                      accountExpiry > now;
        _accountObdExpiresAt = _accountObd ? local!.ExpiresAt : null;
        _accountSource = local?.Source ?? "none";
        _accountProductId = local?.ProductId ?? string.Empty;
        _accountVerified = local?.VerifiedByServer == true;

        LoadAccountVin();

        _appleObd = local?.AppleVerifiedByServer == true &&
                    local.AppleHasObd &&
                    local.AppleObdExpiresAt is { } appleExpiry &&
                    appleExpiry > now;
        _appleVin = local?.AppleVerifiedByServer == true &&
                    local.AppleHasVin &&
                    local.AppleVinExpiresAt is { } appleVinExpiry &&
                    appleVinExpiry > now;
        _appleObdExpiresAt = _appleObd ? local!.AppleObdExpiresAt : null;
        _appleVinExpiresAt = _appleVin ? local!.AppleVinExpiresAt : null;
        _appleProductId = local?.AppleProductId ?? string.Empty;
        _appleVerified = local?.AppleVerifiedByServer == true;

        Publish();
    }

    private void LoadAccountVin()
    {
        try
        {
            var expires = Preferences.Default.Get(AccountVinExpiresKey, 0L);
            var at = expires > 0 ? new DateTime(expires, DateTimeKind.Utc) : (DateTime?)null;
            _accountVin = Preferences.Default.Get(AccountVinKey, false) &&
                          at.HasValue && at.Value > DateTime.UtcNow;
            _accountVinExpiresAt = _accountVin ? at : null;
        }
        catch
        {
            _accountVin = false;
            _accountVinExpiresAt = null;
        }
    }

    private void SaveAccountVin(bool active, DateTime? expires)
    {
        var isActive = active && expires.HasValue && expires.Value > DateTime.UtcNow;
        _accountVin = isActive;
        _accountVinExpiresAt = isActive ? expires : null;
        try
        {
            Preferences.Default.Set(AccountVinKey, isActive);
            Preferences.Default.Set(AccountVinExpiresKey, isActive ? expires!.Value.Ticks : 0L);
        }
        catch
        {
        }
    }

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

            // The account source is only ever read while signed in: on logout
            // there is no token, and a failed call must not wipe a valid cache.
            if (_account.IsSignedIn)
                await LoadServerEntitlementAsync(_account.AccountId);

            // The App Store source belongs to the installation, signed in or not.
            if (AppleEntitlementService.IsSupported)
                await RefreshAppleEntitlementAsync();

            await SaveCacheAsync();
            Publish();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task LoadServerEntitlementAsync(string userId)
    {
        var endpoint = AIConfig.BackendBaseUrl.Trim();
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
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
            if (payload is null)
                return;

            var now = DateTimeOffset.UtcNow;
            var obdExpires = payload.ObdExpiresAt ?? payload.ExpiresAt;
            var obdActive = (payload.HasObd ?? payload.IsPro) && obdExpires.HasValue && obdExpires.Value > now;
            var vinExpires = payload.VinExpiresAt;
            var vinActive = payload.HasVin == true && vinExpires.HasValue && vinExpires.Value > now;

            SaveAccountVin(vinActive, vinExpires?.UtcDateTime);

            _accountVerified = true;
            _accountObd = obdActive;
            _accountObdExpiresAt = obdActive ? obdExpires!.Value.UtcDateTime : null;
            _accountSource = payload.Source ?? "server";
            _accountProductId = payload.ProductId ?? string.Empty;
        }
        catch
        {
            // Preserve only what a previous server answer already verified.
            // No local entitlement is ever manufactured during an outage.
        }
    }

    // ============================================================
    // APPLE (ACCOUNTLESS) SOURCE
    // ============================================================

    private async Task RefreshAppleEntitlementAsync()
    {
        var entitlement = await _appleStore.RefreshAsync();
        if (entitlement is null)
            return;

        ApplyApple(entitlement);
    }

    private void ApplyApple(AppleEntitlement entitlement)
    {
        // A refresh that could not reach Apple tells us nothing. Keeping the
        // last known server-verified state is what stops a network hiccup from
        // switching off a subscription the customer is paying for.
        if (!entitlement.Verified)
            return;

        var now = DateTime.UtcNow;

        _appleObd = entitlement.HasObd &&
                    entitlement.ObdExpiresAt is { } obd &&
                    obd.UtcDateTime > now;
        _appleVin = entitlement.HasVin &&
                    entitlement.VinExpiresAt is { } vin &&
                    vin.UtcDateTime > now;

        _appleObdExpiresAt = _appleObd ? entitlement.ObdExpiresAt!.Value.UtcDateTime : null;
        _appleVinExpiresAt = _appleVin ? entitlement.VinExpiresAt!.Value.UtcDateTime : null;
        _appleProductId = entitlement.ProductId ?? string.Empty;
        _appleVerified = true;
    }

    // ============================================================
    // CACHE / NOTIFICATION
    // ============================================================

    private async Task SaveCacheAsync()
    {
        await _db.SaveProAsync(new ProEntitlement
        {
            Id = "local",
            IsPro = _accountObd,
            VerifiedByServer = _accountVerified,
            Source = _accountSource,
            ExpiresAt = _accountObdExpiresAt,
            ProductId = _accountProductId,
            AppleHasObd = _appleObd,
            AppleHasVin = _appleVin,
            AppleObdExpiresAt = _appleObdExpiresAt,
            AppleVinExpiresAt = _appleVinExpiresAt,
            AppleProductId = _appleProductId,
            AppleVerifiedByServer = _appleVerified,
            AppleVerifiedAt = _appleVerified ? DateTime.UtcNow : null,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private void Publish() => ProChanged?.Invoke(this, IsPro);

    private static DateTime? Latest(DateTime? first, DateTime? second)
    {
        if (first is null) return second;
        if (second is null) return first;
        return first > second ? first : second;
    }

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
    }
}
