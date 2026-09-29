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

    /// <summary>OBD PRO features (from OBD PRO, Full PRO or a promo code).</summary>
    public bool IsPro { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public bool ServerVerified { get; private set; }

    /// <summary>VIN PRO (from VIN PRO, Full PRO or a promo code).</summary>
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

    /// <summary>Why the last promo redemption failed (Georgian | English), empty on success.</summary>
    public (string Ka, string En) LastPromoError { get; private set; } = (string.Empty, string.Empty);

    public async Task<bool> RedeemPromoCodeAsync(string code)
    {
        LastPromoError = (string.Empty, string.Empty);
        var endpoint = AIConfig.BackendBaseUrl.Trim();
        var userId = _account.EffectiveUserId;
        code = (code ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(code) ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            LastPromoError = ("სერვერის მისამართი არ არის მითითებული.", "The server address is not configured.");
            return false;
        }

        // The server links a promo code to an account.
        if (!_account.IsSignedIn)
        {
            LastPromoError = ("პრომო კოდისთვის ჯერ შედი ანგარიშზე (პარამეტრები → ანგარიში).",
                "Sign in to your account first (Settings → Account) to use a promo code.");
            return false;
        }

        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(
                HttpMethod.Post, "/api/promo/redeem");
            request.Content = JsonContent.Create(new { userId, code });
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = string.Empty;
                try { body = await response.Content.ReadAsStringAsync(); } catch { }
                LastPromoError = (int)response.StatusCode switch
                {
                    401 => ("ანგარიშის სესია ვადაგასულია — გამოდი და თავიდან შედი ანგარიშზე.",
                            "Your account session expired — sign out and sign in again."),
                    429 => ("ძალიან ბევრი ცდა — სცადე 15 წუთში.", "Too many attempts — try again in 15 minutes."),
                    400 when body.Contains("already been used", StringComparison.OrdinalIgnoreCase) =>
                        ("ეს კოდი ამ ანგარიშზე უკვე გამოყენებულია.", "This code was already used on this account."),
                    400 => ("კოდი არასწორია, ვადა გაუვიდა ან ლიმიტი ამოიწურა.", "The code is invalid, expired or fully used."),
                    404 => ("სერვერზე პრომო კოდის ფუნქცია არ არის (Backend-ის განახლებაა საჭირო).", "The server has no promo endpoint (the backend needs updating)."),
                    _ => ($"სერვერის შეცდომა ({(int)response.StatusCode}). სცადე მოგვიანებით.", $"Server error ({(int)response.StatusCode}). Try again later.")
                };
                return false;
            }

            await LoadAsync(forceServerRefresh: true);
            var active = (IsPro && ServerVerified) || HasVin;
            if (!active)
                LastPromoError = ("კოდი მიღებულია, მაგრამ PRO ვერ ჩაიტვირთა — დახურე და თავიდან გახსენი PRO გვერდი.",
                    "The code was accepted but PRO did not load — reopen the PRO page.");
            return active;
        }
        catch
        {
            LastPromoError = ("სერვერთან კავშირი ვერ დამყარდა. შეამოწმე ინტერნეტი.", "Could not reach the server. Check the internet connection.");
            return false;
        }
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
                await _db.SaveProAsync(new ProEntitlement { Id = "local", IsPro = false, VerifiedByServer = true, Source = payload.Source ?? "server", ProductId = payload.ProductId, UpdatedAt = DateTime.UtcNow });
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
