using Microsoft.Maui.Storage;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CarScanner.Services.OBD;

namespace CarScanner.Services;

public sealed class AccountService
{
    private const string AccountIdKey = "account_user_id";
    private const string AccountEmailKey = "account_email";
    private const string AccessTokenKey = "account_access_token";
    private const string DeviceIdKey = "account_device_id";
    private const string MustChangePasswordKey = "account_must_change_password";

    /// <summary>Login refused because the account is linked to another phone.</summary>
    public const string DeviceAlreadyBoundCode = "DEVICE_ALREADY_BOUND";

    /// <summary>Moving the account to another phone is allowed once per 7 days.</summary>
    public const string RebindCooldownCode = "REBIND_COOLDOWN";

    private static string L(string ka, string en) => ObdLang.L(ka, en);

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

    /// <summary>
    /// Stable, opaque id of the signed-in account for store purchases
    /// (iOS StoreKit applicationUsername): SHA-256 of the account id, lower-case
    /// hex. Null when not signed in. The server can compute the same value to
    /// bind a purchase to the account.
    /// </summary>
    public string? HashedAccountId
    {
        get
        {
            var accountId = AccountId;
            if (!Guid.TryParse(accountId, out _))
                return null;

            var hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(accountId));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }

    public string EffectiveUserId =>
        IsSignedIn
            ? AccountId
            : EnsureInstallationId();

    /// <summary>
    /// True when the last sign-in answered "mustChangePassword": true (the
    /// password was a temporary one set by support). Cleared after a
    /// successful <see cref="ChangePasswordAsync"/> or on sign-out.
    /// </summary>
    public bool MustChangePassword =>
        IsSignedIn &&
        Preferences.Default.Get(
            MustChangePasswordKey,
            false);

    public event EventHandler? AccountChanged;

    public Task<AccountActionResult> RegisterAsync(
        string email,
        string password,
        CancellationToken token = default) =>
        AuthenticateAsync(
            "/api/accounts/register",
            email,
            password,
            moveDevice: false,
            token);

    /// <summary>
    /// Signs in. When the account is linked to another phone the result has
    /// <see cref="AccountActionResult.Code"/> = <see cref="DeviceAlreadyBoundCode"/>;
    /// after the user confirms, call again with <paramref name="moveDevice"/> = true
    /// to move the account to this phone.
    /// </summary>
    public Task<AccountActionResult> LoginAsync(
        string email,
        string password,
        bool moveDevice = false,
        CancellationToken token = default) =>
        AuthenticateAsync(
            "/api/accounts/login",
            email,
            password,
            moveDevice,
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

            Preferences.Default.Remove(
                MustChangePasswordKey);

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
                    response.StatusCode == HttpStatusCode.Unauthorized
                        ? L("სესია ვადაგასულია. გამოდი, თავიდან შედი ანგარიშზე და ისევ სცადე წაშლა.",
                            "Your session has expired. Sign out, sign in again and then delete the account.")
                        : (int)response.StatusCode >= 500
                            ? L("სერვერი დროებით მიუწვდომელია. ანგარიში არ წაშლილა — სცადე მოგვიანებით.",
                                "The server is temporarily unavailable. The account was not deleted — try again later.")
                            : L("ანგარიშის წაშლა ვერ შესრულდა. სცადე მოგვიანებით.",
                                "The account could not be deleted. Try again later."));
            }

            SecureStorage.Default.Remove(
                AccessTokenKey);

            Preferences.Default.Remove(
                AccountIdKey);

            Preferences.Default.Remove(
                AccountEmailKey);

            Preferences.Default.Remove(
                MustChangePasswordKey);

            AccountChanged?.Invoke(
                this,
                EventArgs.Empty);

            return new(
                true,
                L("ანგარიში და მასთან დაკავშირებული სერვერის მონაცემები წაიშალა.",
                  "Your account and its data on our server were deleted."));
        }
        catch
        {
            return new(
                false,
                ServerUnavailableMessage());
        }
    }

    /// <summary>
    /// Changes the password of the signed-in account
    /// (POST /api/accounts/password, same auth headers as the other account calls).
    /// </summary>
    public async Task<AccountActionResult> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken token = default)
    {
        if (!IsSignedIn)
        {
            return new(
                false,
                L("ჯერ შედი ანგარიშზე.", "Sign in first."));
        }

        if (string.IsNullOrEmpty(currentPassword) ||
            string.IsNullOrEmpty(newPassword))
        {
            return new(
                false,
                L("შეიყვანე მიმდინარე და ახალი პაროლი.", "Enter the current and the new password."));
        }

        if (newPassword.Length < 10 ||
            newPassword.Length > 128)
        {
            return new(
                false,
                L("პაროლი 10-დან 128 სიმბოლომდე უნდა იყოს.",
                  "The password must have 10 to 128 characters."));
        }

        try
        {
            using var request =
                await CreateAuthorizedRequestAsync(
                    HttpMethod.Post,
                    "/api/accounts/password");

            request.Content =
                JsonContent.Create(
                    new
                    {
                        currentPassword,
                        newPassword
                    });

            using var response =
                await _http.SendAsync(
                    request,
                    token);

            if (!response.IsSuccessStatusCode)
            {
                var (_, serverMessage) =
                    await ReadErrorAsync(
                        response,
                        token);

                var statusCode = (int)response.StatusCode;

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return new(
                        false,
                        L("მიმდინარე პაროლი არასწორია ან სესია ვადაგასულია. შეამოწმე პაროლი; საჭიროების შემთხვევაში გამოდი და თავიდან შედი.",
                          "The current password is wrong or the session has expired. Check the password; if needed, sign out and sign in again."));
                }

                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var text = (serverMessage ?? string.Empty).Trim();
                    return new(
                        false,
                        !string.IsNullOrWhiteSpace(text) && text.Length <= 300
                            ? L("ახალი პაროლი არ აკმაყოფილებს მოთხოვნებს (10–128 სიმბოლო, არ უნდა ემთხვეოდეს ძველს).",
                                text)
                            : L("ახალი პაროლი არ აკმაყოფილებს მოთხოვნებს (10–128 სიმბოლო).",
                                "The new password does not meet the requirements (10–128 characters)."));
                }

                if (statusCode == 429)
                {
                    return new(
                        false,
                        L("ძალიან ბევრი მცდელობაა. დაელოდე რამდენიმე წუთს და სცადე თავიდან.",
                          "Too many attempts. Wait a few minutes and try again."));
                }

                return new(
                    false,
                    statusCode >= 500
                        ? L("სერვერი დროებით მიუწვდომელია. პაროლი არ შეცვლილა — სცადე მოგვიანებით.",
                            "The server is temporarily unavailable. The password was not changed — try again later.")
                        : L("პაროლის შეცვლა ვერ მოხერხდა. სცადე მოგვიანებით.",
                            "The password could not be changed. Try again later."));
            }

            // If the server rotates the session on password change, keep the new token.
            try
            {
                var payload =
                    await response.Content
                        .ReadFromJsonAsync<ChangePasswordResponse>(
                            cancellationToken: token);

                if (!string.IsNullOrWhiteSpace(
                        payload?.AccessToken))
                {
                    await SecureStorage.Default.SetAsync(
                        AccessTokenKey,
                        payload!.AccessToken!);
                }
            }
            catch
            {
                // Empty or non-JSON 200 body: nothing to update.
            }

            Preferences.Default.Remove(
                MustChangePasswordKey);

            return new(
                true,
                L("პაროლი შეიცვალა.", "Your password was changed."));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(
                false,
                L("შეჩერდა.", "Stopped."));
        }
        catch
        {
            return new(
                false,
                ServerUnavailableMessage());
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
        bool moveDevice,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            return new(
                false,
                L("ელ-ფოსტა და პაროლი აუცილებელია.", "E-mail and password are required."));
        }

        var isRegister =
            path.Contains("register",
                StringComparison.OrdinalIgnoreCase);

        try
        {
            var deviceId =
                await GetDeviceIdAsync();

            using var request =
                await CreateAuthorizedRequestAsync(
                    HttpMethod.Post,
                    path);

            // "moveDevice" is only sent after the user confirmed moving the
            // account from the other phone to this one.
            request.Content =
                moveDevice
                    ? JsonContent.Create(
                        new
                        {
                            email = email.Trim(),
                            password,
                            deviceId,
                            moveDevice = true
                        })
                    : JsonContent.Create(
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
                var (code, serverMessage) =
                    await ReadErrorAsync(
                        response,
                        token);

                return DescribeFailure(
                    response.StatusCode,
                    code,
                    serverMessage,
                    isRegister,
                    moveDevice);
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
                    ServerUnavailableMessage());
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

            Preferences.Default.Set(
                MustChangePasswordKey,
                payload.MustChangePassword == true);

            AccountChanged?.Invoke(
                this,
                EventArgs.Empty);

            return new(
                true,
                L("ანგარიში წარმატებით დაუკავშირდა.", "Signed in to your account."));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(
                false,
                L("შეჩერდა.", "Stopped."));
        }
        catch
        {
            // Network error, timeout or no secure backend.
            return new(
                false,
                ServerUnavailableMessage());
        }
    }

    private static string ServerUnavailableMessage() =>
        L("ანგარიშის სერვერთან დაკავშირება ვერ მოხერხდა. შეამოწმე ინტერნეტი და სცადე მოგვიანებით.",
          "Could not reach the account server. Check the internet and try again later.");

    /// <summary>Turns a failed register / login answer into a clear message.</summary>
    private static AccountActionResult DescribeFailure(
        HttpStatusCode status,
        string? code,
        string? serverMessage,
        bool isRegister,
        bool moveDevice)
    {
        var statusCode = (int)status;

        if (string.Equals(code, DeviceAlreadyBoundCode, StringComparison.OrdinalIgnoreCase) ||
            (!isRegister && status == HttpStatusCode.Conflict))
        {
            return new(
                false,
                moveDevice
                    ? L("ანგარიშის ამ ტელეფონზე გადმოტანა ახლა ვერ მოხერხდა. სცადე მოგვიანებით ან მოგვწერე: gcar360support@gmail.com",
                        "The account could not be moved to this phone right now. Try again later or write to gcar360support@gmail.com")
                    : L("ეს ანგარიში სხვა ტელეფონზეა მიბმული. თავიდან შესვლით შეგიძლია ის ამ ტელეფონზე გადმოიტანო.",
                        "This account is linked to another phone. Sign in again to move it to this phone."),
                DeviceAlreadyBoundCode);
        }

        if (isRegister && status == HttpStatusCode.Conflict)
        {
            return new(
                false,
                L("ამ ელ-ფოსტით ანგარიში უკვე არსებობს. შედი ანგარიშზე.",
                  "An account with this e-mail already exists. Sign in instead."));
        }

        if (status == HttpStatusCode.BadRequest)
        {
            var text = serverMessage ?? string.Empty;
            if (text.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    false,
                    L("პაროლი 10-დან 128 სიმბოლომდე უნდა იყოს.",
                      "The password must have 10 to 128 characters."));
            }

            if (text.Contains("email", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    false,
                    L("შეიყვანე სწორი ელ-ფოსტის მისამართი.",
                      "Enter a valid e-mail address."));
            }

            // Other validation text from the server (English only).
            if (!string.IsNullOrWhiteSpace(text) && text.Length <= 300)
            {
                return new(
                    false,
                    L("შეამოწმე ელ-ფოსტა და პაროლი (პაროლი — მინიმუმ 10 სიმბოლო).",
                      text.Trim()));
            }

            return new(
                false,
                isRegister
                    ? L("ანგარიში ვერ შეიქმნა. შეამოწმე ელ-ფოსტა და პაროლი (მინიმუმ 10 სიმბოლო).",
                        "The account could not be created. Check the e-mail and the password (at least 10 characters).")
                    : L("შეამოწმე ელ-ფოსტა და პაროლი.",
                        "Check the e-mail and password."));
        }

        if (status == HttpStatusCode.Unauthorized)
        {
            return new(
                false,
                L("ელ-ფოსტა ან პაროლი არასწორია.",
                  "Wrong e-mail or password."));
        }

        if (string.Equals(code, RebindCooldownCode, StringComparison.OrdinalIgnoreCase))
        {
            return new(
                false,
                L("ანგარიში ახლახან გადავიდა სხვა ტელეფონზე. ანგარიშის გადმოტანა შესაძლებელია 7 დღეში ერთხელ — სცადე მოგვიანებით ან მოგვწერე: gcar360support@gmail.com",
                  "The account was moved to another phone recently. An account can be moved once per 7 days — try again later or write to gcar360support@gmail.com"),
                RebindCooldownCode);
        }

        if (statusCode == 429)
        {
            return new(
                false,
                L("ძალიან ბევრი მცდელობაა. დაელოდე რამდენიმე წუთს და სცადე თავიდან.",
                  "Too many attempts. Wait a few minutes and try again."));
        }

        if (statusCode >= 500)
        {
            return new(
                false,
                L("სერვერი დროებით მიუწვდომელია. სცადე მოგვიანებით.",
                  "The server is temporarily unavailable. Try again later."));
        }

        return new(
            false,
            isRegister
                ? L("ანგარიში ვერ შეიქმნა. სცადე მოგვიანებით.",
                    "The account could not be created. Try again later.")
                : L("შესვლა ვერ მოხერხდა. სცადე მოგვიანებით.",
                    "Could not sign in. Try again later."));
    }

    /// <summary>Reads "code" and "message" (or "error" / "detail") from an error answer.</summary>
    private static async Task<(string? Code, string? Message)> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken token)
    {
        try
        {
            var body =
                await response.Content.ReadAsStringAsync(
                    token);

            if (string.IsNullOrWhiteSpace(body) ||
                body.Length > 16_384)
            {
                return (null, null);
            }

            using var document =
                JsonDocument.Parse(body);

            if (document.RootElement.ValueKind !=
                JsonValueKind.Object)
            {
                return (null, null);
            }

            string? Read(string name)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }
                }

                return null;
            }

            return (
                Read("code"),
                Read("message") ?? Read("error") ?? Read("detail"));
        }
        catch
        {
            return (null, null);
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

        public bool? MustChangePassword { get; set; }
    }

    private sealed class ChangePasswordResponse
    {
        public string? AccessToken { get; set; }
    }
}

public sealed record AccountActionResult(
    bool Success,
    string Message,
    string? Code = null);