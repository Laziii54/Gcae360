using Microsoft.Maui.Storage;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CarScanner.Services;

public sealed class AccountService
{
    private const string AccountIdKey = "account_user_id";
    private const string AccountEmailKey = "account_email";
    private const string AccessTokenKey = "account_access_token";
    private const string DeviceIdKey = "account_device_id";

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public string AccountId =>
        Preferences.Default.Get(
            AccountIdKey,
            string.Empty);

    public string Email =>
        Preferences.Default.Get(
            AccountEmailKey,
            string.Empty);

    public bool IsSignedIn =>
        Guid.TryParse(AccountId, out _);

    public string EffectiveUserId =>
        IsSignedIn
            ? AccountId
            : EnsureInstallationId();

    public event EventHandler? AccountChanged;

    public Task<AccountActionResult> RegisterAsync(
        string email,
        string password,
        CancellationToken token = default) =>
        AuthenticateAsync(
            "/api/accounts/register",
            email,
            password,
            token);

    public Task<AccountActionResult> LoginAsync(
        string email,
        string password,
        CancellationToken token = default) =>
        AuthenticateAsync(
            "/api/accounts/login",
            email,
            password,
            token);

    public async Task<string> GetDeviceIdAsync()
    {
        var existing =
            await SecureStorage.Default.GetAsync(
                DeviceIdKey);

        if (Guid.TryParseExact(
                existing,
                "N",
                out _))
        {
            return existing!;
        }

        // Stable across reinstalls: on Android the ID is derived from
        // Settings.Secure.ANDROID_ID (per device + signing key), so deleting
        // and reinstalling the app does not look like "another device".
        var deviceId =
            StableDeviceId() ??
            Guid.NewGuid()
                .ToString("N");

        await SecureStorage.Default.SetAsync(
            DeviceIdKey,
            deviceId);

        return deviceId;
    }

    private static string? StableDeviceId()
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var androidId = Android.Provider.Settings.Secure.GetString(
                context.ContentResolver,
                Android.Provider.Settings.Secure.AndroidId);
            if (string.IsNullOrWhiteSpace(androidId) || androidId == "9774d56d682e549c")
                return null;

            var hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("gcar360-device:" + androidId));
            return new Guid(hash.AsSpan(0, 16)).ToString("N");
        }
        catch
        {
            return null;
        }
#else
        return null;
#endif
    }

    public async Task LogoutAsync(
        CancellationToken token = default)
    {
        try
        {
            using var request =
                await CreateAuthorizedRequestAsync(
                    HttpMethod.Post,
                    "/api/accounts/logout");

            await _http.SendAsync(
                request,
                token);
        }
        catch
        {
        }
        finally
        {
            SecureStorage.Default.Remove(
                AccessTokenKey);

            Preferences.Default.Remove(
                AccountIdKey);

            Preferences.Default.Remove(
                AccountEmailKey);

            AccountChanged?.Invoke(
                this,
                EventArgs.Empty);
        }
    }

    public async Task<AccountActionResult> DeleteAccountAsync(
        CancellationToken token = default)
    {
        try
        {
            using var request =
                await CreateAuthorizedRequestAsync(
                    HttpMethod.Delete,
                    "/api/accounts/me");

            using var response =
                await _http.SendAsync(
                    request,
                    token);

            if (!response.IsSuccessStatusCode)
            {
                return new(
                    false,
                    "ანგარიშის წაშლა ვერ შესრულდა.");
            }

            SecureStorage.Default.Remove(
                AccessTokenKey);

            Preferences.Default.Remove(
                AccountIdKey);

            Preferences.Default.Remove(
                AccountEmailKey);

            AccountChanged?.Invoke(
                this,
                EventArgs.Empty);

            return new(
                true,
                "ანგარიში და მასთან დაკავშირებული სერვერის მონაცემები წაიშალა.");
        }
        catch
        {
            return new(
                false,
                "ანგარიშის სერვერთან დაკავშირება ვერ მოხერხდა.");
        }
    }

    public async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(
        HttpMethod method,
        string relativePath)
    {
        if (!Uri.TryCreate(
                AIConfig.BackendBaseUrl,
                UriKind.Absolute,
                out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Secure backend is unavailable.");
        }

        var request =
            new HttpRequestMessage(
                method,
                new Uri(
                    baseUri,
                    relativePath));

        var accessToken =
            await SecureStorage.Default.GetAsync(
                AccessTokenKey);

        if (!string.IsNullOrWhiteSpace(
                accessToken))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);
        }

        var deviceId =
            await GetDeviceIdAsync();

        request.Headers.TryAddWithoutValidation(
            "X-GCS-Device-Id",
            deviceId);

        return request;
    }

    private async Task<AccountActionResult> AuthenticateAsync(
        string path,
        string email,
        string password,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            return new(
                false,
                "Email და პაროლი აუცილებელია.");
        }

        try
        {
            var deviceId =
                await GetDeviceIdAsync();

            using var request =
                await CreateAuthorizedRequestAsync(
                    HttpMethod.Post,
                    path);

            request.Content =
                JsonContent.Create(
                    new
                    {
                        email = email.Trim(),
                        password,
                        deviceId
                    });

            using var response =
                await _http.SendAsync(
                    request,
                    token);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode ==
                    System.Net.HttpStatusCode.Conflict)
                {
                    return new(
                        false,
                        path.Contains("register",
                            StringComparison.OrdinalIgnoreCase)
                            ? "ამ Email-ით ანგარიში უკვე არსებობს."
                            : "ეს ანგარიში უკვე სხვა მოწყობილობაზეა მიბმული.");
                }

                return new(
                    false,
                    "Email ან პაროლი არასწორია.");
            }

            var payload =
                await response.Content
                    .ReadFromJsonAsync<AccountResponse>(
                        cancellationToken: token);

            if (payload is null ||
                !Guid.TryParse(
                    payload.AccountId,
                    out _) ||
                string.IsNullOrWhiteSpace(
                    payload.AccessToken))
            {
                return new(
                    false,
                    "სერვერის პასუხი არასწორია.");
            }

            await SecureStorage.Default.SetAsync(
                AccessTokenKey,
                payload.AccessToken);

            Preferences.Default.Set(
                AccountIdKey,
                payload.AccountId);

            Preferences.Default.Set(
                AccountEmailKey,
                payload.Email ?? email.Trim());

            AccountChanged?.Invoke(
                this,
                EventArgs.Empty);

            return new(
                true,
                "ანგარიში წარმატებით დაუკავშირდა.");
        }
        catch
        {
            return new(
                false,
                "ანგარიშის სერვერთან დაკავშირება ვერ მოხერხდა.");
        }
    }

    private static string EnsureInstallationId()
    {
        var userId =
            Preferences.Default.Get(
                "user_id",
                string.Empty);

        if (!Guid.TryParse(
                userId,
                out _))
        {
            userId =
                Guid.NewGuid()
                    .ToString("N");

            Preferences.Default.Set(
                "user_id",
                userId);
        }

        return userId;
    }

    private sealed class AccountResponse
    {
        public string? AccountId { get; set; }

        public string? Email { get; set; }

        public string? AccessToken { get; set; }
    }
}

public sealed record AccountActionResult(
    bool Success,
    string Message);