using CarScanner.Database;
using CarScanner.Models;
using System.Net.Http.Json;

namespace CarScanner.Services;

public sealed class ProService
{
    private readonly AppDatabase _db;
    private readonly AccountService _account;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SemaphoreSlim _serverRefreshGate = new(1, 1);
    private DateTimeOffset _lastServerRefreshAttempt = DateTimeOffset.MinValue;
    private static readonly TimeSpan ServerRefreshInterval =
        TimeSpan.FromMinutes(5);
    // Subscriptions (monthly only).
    public const string ObdProductId = "com.georgiancarscanner.pro.monthly";
    public const string VinProductId = "com.georgiancarscanner.vin.monthly";
    public const string FullProductId = "com.georgiancarscanner.full.monthly";
    public const string ProductId = ObdProductId;

    // One-time VIN credits.
    public const string VinPack5ProductId = "com.georgiancarscanner.vin.pack5";
    public const string VinPack10ProductId = "com.georgiancarscanner.vin.pack10";
    public const string VinSingleProductId = "com.georgiancarscanner.vin.single";

    // Shown only when the store price cannot be loaded.
    public const decimal PriceGEL = 4.99m;
    public const decimal VinPriceGEL = 5.99m;
    public const decimal FullPriceGEL = 8.99m;
    public const decimal VinPack5PriceGEL = 3.99m;
    public const decimal VinPack10PriceGEL = 6.99m;
    public const decimal VinSinglePriceGEL = 2.99m;
    public const int VinMonthlyReports = 10;

    private const string HasVinKey = "pro.hasVin";
    private const string VinExpiresKey = "pro.vinExpires";

    /// <summary>OBD PRO features (from OBD PRO, Full PRO or a plan given by the admin).</summary>
    public bool IsPro { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public bool ServerVerified { get; private set; }

    /// <summary>VIN PRO (from VIN PRO, Full PRO or a plan given by the admin).</summary>
    public bool HasVin { get; private set; }
    public DateTime? VinExpiresAt { get; private set; }

    public bool HasObd => IsPro && ServerVerified;

    /// <summary>none, obd, vin or full.</summary>
    public string Tier => HasObd && HasVin ? "full" : HasObd ? "obd" : HasVin ? "vin" : "none";

    public event EventHandler<bool>? ProChanged;

    public ProService(AppDatabase db, AccountService account)
    {
        _db = db;
        _account = account;
        _account.AccountChanged += OnAccountChanged;
    }

    /// <summary>
    /// PRO is per-account, never implied by "no session": without this, signing out (or deleting the account)
    /// left the previously cached local entitlement active, because a server refresh attempted right after
    /// logout has no access token any more, fails silently, and LoadAsync's failure path deliberately preserves
    /// the existing cache (to survive real outages) — which also preserved it across a sign-out. AccountChanged
    /// fires only after AccountService has already cleared the stored session, so IsSignedIn is reliable here.
    /// </summary>
    private async void OnAccountChanged(object? sender, EventArgs e)
    {
        if (_account.IsSignedIn)
            return;

        try { await DeactivateAsync(); } catch { }
    }

    public async Task LoadAsync(bool forceServerRefresh = false)
    {
        var userId = _account.EffectiveUserId;
        var local = await _db.GetProAsync();
        var localActive = local?.IsPro == true && local.ExpiresAt.HasValue && local.ExpiresAt.Value > DateTime.UtcNow && local.VerifiedByServer;
        IsPro = localActive;
        ExpiresAt = local?.ExpiresAt;
        ServerVerified = local?.VerifiedByServer == true;
        LoadLocalVin();

        if (!forceServerRefresh &&
            DateTimeOffset.UtcNow - _lastServerRefreshAttempt <
                ServerRefreshInterval)
        {
            return;
        }

        if ((localActive || HasVin) && !forceServerRefresh)
        {
            // A valid, server-verified cached entitlement makes local PRO
            // features instant. Revocation/renewal is refreshed in the
            // background and AI is still independently authorized server-side.
            _ = RefreshServerEntitlementAsync(
                userId,
                force: false);
            return;
        }

        await RefreshServerEntitlementAsync(
            userId,
            forceServerRefresh);
    }

    public async Task DeactivateAsync()
    {
        IsPro = false; ExpiresAt = null; ServerVerified = false;
        SaveLocalVin(false, null);
        await _db.SaveProAsync(new ProEntitlement { Id = "local", IsPro = false, VerifiedByServer = false, UpdatedAt = DateTime.UtcNow });
        ProChanged?.Invoke(this, false);
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

    private void LoadLocalVin()
    {
        try
        {
            var expires = Preferences.Default.Get(VinExpiresKey, 0L);
            VinExpiresAt = expires > 0 ? new DateTime(expires, DateTimeKind.Utc) : null;
            HasVin = Preferences.Default.Get(HasVinKey, false) &&
                     VinExpiresAt.HasValue && VinExpiresAt.Value > DateTime.UtcNow;
        }
        catch
        {
            HasVin = false;
        }
    }

    private void SaveLocalVin(bool active, DateTime? expires)
    {
        HasVin = active && expires.HasValue && expires.Value > DateTime.UtcNow;
        VinExpiresAt = HasVin ? expires : null;
        try
        {
            Preferences.Default.Set(HasVinKey, HasVin);
            Preferences.Default.Set(VinExpiresKey, HasVin ? expires!.Value.Ticks : 0L);
        }
        catch
        {
        }
    }

    public bool CanUse(string feature) => IsPro && ServerVerified;

    private async Task RefreshServerEntitlementAsync(
        string userId,
        bool force)
    {
        await _serverRefreshGate.WaitAsync();
        try
        {
            if (!force &&
                DateTimeOffset.UtcNow - _lastServerRefreshAttempt <
                    ServerRefreshInterval)
            {
                return;
            }

            _lastServerRefreshAttempt = DateTimeOffset.UtcNow;
            await LoadServerEntitlementAsync(userId);
        }
        finally
        {
            _serverRefreshGate.Release();
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

            SaveLocalVin(vinActive, vinExpires?.UtcDateTime);

            if (obdActive)
            {
                var expires = obdExpires!.Value.UtcDateTime;
                IsPro = true; ExpiresAt = expires; ServerVerified = true;
                await _db.SaveProAsync(new ProEntitlement { Id = "local", IsPro = true, VerifiedByServer = true, Source = payload.Source ?? "server", ProductId = payload.ProductId ?? ProductId, ExpiresAt = expires, UpdatedAt = DateTime.UtcNow });
                ProChanged?.Invoke(this, true);
            }
            else if (vinActive)
            {
                IsPro = false; ExpiresAt = null; ServerVerified = true;
                await _db.SaveProAsync(new ProEntitlement { Id = "local", IsPro = false, VerifiedByServer = true, Source = payload.Source ?? "server", ProductId = payload.ProductId ?? string.Empty, UpdatedAt = DateTime.UtcNow });
                ProChanged?.Invoke(this, false);
            }
            else
            {
                await DeactivateAsync();
            }
        }
        catch
        {
            // Preserve only an unexpired entitlement that was previously
            // verified and stored by the server. No local entitlement is ever
            // manufactured during an outage.
        }
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
