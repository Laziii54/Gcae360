using Microsoft.Maui.Storage;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CarScanner.Services.ExternalSignIn;
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

    /// <summary>
    /// What the server said about the verification email right after a registration: "sent" (the mail server accepted
    /// it), "failed" (it could not be sent) or "unavailable" (the server cannot send email). Null for a sign-in.
    /// </summary>
    public string? EmailVerificationState { get; private set; }

    /// <summary>
    /// Server-confirmed only: true when this is the account configured as admin AND it has completed a 2-step
    /// sign-in on the admin page at least once. A privileged feature must check this, never a local e-mail string.
    /// False (never throws) when signed out, offline, or the server disagrees.
    /// </summary>
    public async Task<bool> IsAdminAsync(CancellationToken token = default)
    {
        if (!IsSignedIn)
            return false;
        try
        {
            using var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, "/api/accounts/me");
            using var response = await _http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                return false;

            var body = await response.Content.ReadFromJsonAsync<MeResponse>(cancellationToken: token);
            return body?.IsAdmin == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Whether this account's email is verified, whether a code can be sent, and whether the server asks for it.</summary>
    public async Task<EmailVerificationStatus?> GetEmailStatusAsync(CancellationToken token = default)
    {
        try
        {
            using var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, "/api/accounts/email/status");
            using var response = await _http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadFromJsonAsync<EmailStatusResponse>(cancellationToken: token);
            return body is null ? null : new EmailVerificationStatus(body.Verified == true, body.CanSend == true, body.Required == true);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Asks the server to send a (new) verification code. The server limits this to one per minute.</summary>
    public async Task<AccountActionResult> SendEmailCodeAsync(CancellationToken token = default)
    {
        try
        {
            using var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/accounts/email/send-code");
            using var response = await _http.SendAsync(request, token);
            var body = await ReadEmailStatusAsync(response, token);

            if (response.IsSuccessStatusCode)
            {
                return body?.Status == "already_verified"
                    ? new(true, L("ეს ელფოსტა უკვე დადასტურებულია.", "This email address is already verified."), "already_verified")
                    : new(true, L("ახალი კოდი გაიგზავნა ელფოსტაზე.", "A new code was sent to your email."));
            }

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.TooManyRequests => new(false, L("ცოტა ხანს დაიცადე და სცადე თავიდან.", "Please wait a moment and try again.")),
                System.Net.HttpStatusCode.ServiceUnavailable => new(false, L("ელფოსტის გაგზავნა ამჟამად მიუწვდომელია.", "Sending email is not available right now.")),
                System.Net.HttpStatusCode.BadGateway => new(false, L("დამადასტურებელი წერილი ვერ გაიგზავნა. სცადე ცოტა ხანში.", "We couldn't send the verification email. Please try again in a moment.")),
                System.Net.HttpStatusCode.Unauthorized => new(false, ServerUnavailableMessage()),
                _ => new(false, ServerUnavailableMessage())
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(false, L("შეჩერდა.", "Stopped."));
        }
        catch
        {
            return new(false, ServerUnavailableMessage());
        }
    }

    /// <summary>Checks the 6-digit code from the email. Only the server decides whether it is right.</summary>
    public async Task<AccountActionResult> VerifyEmailCodeAsync(string code, CancellationToken token = default)
    {
        try
        {
            using var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/accounts/email/verify");
            request.Content = JsonContent.Create(new { code = (code ?? string.Empty).Trim() });
            using var response = await _http.SendAsync(request, token);
            var body = await ReadEmailStatusAsync(response, token);

            if (response.IsSuccessStatusCode && body?.Verified == true)
                return new(true, L("ელფოსტა დადასტურებულია.", "Email verified."));

            return body?.Reason switch
            {
                "expired" => new(false, L("კოდს ვადა გაუვიდა. მოითხოვე ახალი.", "The code has expired. Request a new one."), "expired"),
                "too_many_attempts" => new(false, L("ძალიან ბევრი მცდელობა. მოითხოვე ახალი კოდი.", "Too many attempts. Request a new code."), "too_many_attempts"),
                "no_code" => new(false, L("აქტიური კოდი არ არის. მოითხოვე ახალი.", "There is no active code. Request a new one."), "no_code"),
                "invalid" => new(false, L("კოდი არასწორია. სცადე თავიდან.", "That code is not correct. Try again."), "invalid"),
                _ => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? new(false, L("ცოტა ხანს დაიცადე და სცადე თავიდან.", "Please wait a moment and try again."))
                    : new(false, ServerUnavailableMessage())
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(false, L("შეჩერდა.", "Stopped."));
        }
        catch
        {
            return new(false, ServerUnavailableMessage());
        }
    }

    private static async Task<EmailStatusResponse?> ReadEmailStatusAsync(HttpResponseMessage response, CancellationToken token)
    {
        try { return await response.Content.ReadFromJsonAsync<EmailStatusResponse>(cancellationToken: token); }
        catch { return null; }
    }

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

        if (newPassword.Length < 8 ||
            newPassword.Length > 128)
        {
            return new(
                false,
                L("პაროლი 8-დან 128 სიმბოლომდე უნდა იყოს.",
                  "The password must have 8 to 128 characters."));
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
                            ? L("ახალი პაროლი არ აკმაყოფილებს მოთხოვნებს (8–128 სიმბოლო, არ უნდა ემთხვეოდეს ძველს).",
                                text)
                            : L("ახალი პაროლი არ აკმაყოფილებს მოთხოვნებს (8–128 სიმბოლო).",
                                "The new password does not meet the requirements (8–128 characters)."));
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

        request.Headers.TryAddWithoutValidation(
            "X-GCS-Language",
            CarScanner.Services.Localization.LanguageCatalog.Current.Code);

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

        EmailVerificationState = null;

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

            // Login only: a second-factor code was just emailed, no session yet — AccountPage asks for it and
            // calls VerifyLoginCodeAsync, which finishes the sign-in exactly as below.
            if (!isRegister && payload?.NeedsCode == true)
                return new(false, string.Empty, NeedsLoginCodeCode);

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

            return await ApplySessionAsync(payload, email, markEmailVerificationState: isRegister);
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

    /// <summary>Stores the session from a successful register/login/code-verify answer and raises <see cref="AccountChanged"/>.</summary>
    private async Task<AccountActionResult> ApplySessionAsync(AccountResponse payload, string email, bool markEmailVerificationState)
    {
        await SecureStorage.Default.SetAsync(AccessTokenKey, payload.AccessToken!);
        Preferences.Default.Set(AccountIdKey, payload.AccountId!);
        Preferences.Default.Set(AccountEmailKey, payload.Email ?? email.Trim());
        Preferences.Default.Set(MustChangePasswordKey, payload.MustChangePassword == true);

        if (markEmailVerificationState)
            EmailVerificationState = payload.EmailVerification?.Trim().ToLowerInvariant();

        AccountChanged?.Invoke(this, EventArgs.Empty);

        return new(true, L("ანგარიში წარმატებით დაუკავშირდა.", "Signed in to your account."));
    }

    /// <summary><see cref="AccountActionResult.Code"/> when login answered "a code was just emailed, no session yet".</summary>
    public const string NeedsLoginCodeCode = "NEEDS_LOGIN_CODE";

    /// <summary>
    /// Second step of login: the 6-digit code just emailed to the account (see <see cref="NeedsLoginCodeCode"/>).
    /// The server re-checks the password itself — the code alone is never enough.
    /// </summary>
    public async Task<AccountActionResult> VerifyLoginCodeAsync(string email, string password, string code, bool moveDevice, CancellationToken token = default)
    {
        try
        {
            using var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/accounts/login/verify-code");
            request.Content = JsonContent.Create(new
            {
                email = email.Trim(),
                password,
                code = (code ?? string.Empty).Trim(),
                moveDevice
            });

            using var response = await _http.SendAsync(request, token);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var body = await ReadEmailStatusAsync(response, token);
                    return body?.Reason switch
                    {
                        "expired" => new(false, L("კოდს ვადა გაუვიდა. მოითხოვე ახალი.", "The code has expired. Request a new one."), "expired"),
                        "too_many_attempts" => new(false, L("ძალიან ბევრი მცდელობა. მოითხოვე ახალი კოდი.", "Too many attempts. Request a new code."), "too_many_attempts"),
                        "no_code" => new(false, L("აქტიური კოდი არ არის. მოითხოვე ახალი.", "There is no active code. Request a new one."), "no_code"),
                        _ => new(false, L("კოდი არასწორია. სცადე თავიდან.", "That code is not correct. Try again."), "invalid")
                    };
                }

                var (code2, serverMessage) = await ReadErrorAsync(response, token);
                return DescribeFailure(response.StatusCode, code2, serverMessage, isRegister: false, moveDevice);
            }

            var payload = await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken: token);
            if (payload is null || !Guid.TryParse(payload.AccountId, out _) || string.IsNullOrWhiteSpace(payload.AccessToken))
                return new(false, ServerUnavailableMessage());

            return await ApplySessionAsync(payload, email, markEmailVerificationState: false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(false, L("შეჩერდა.", "Stopped."));
        }
        catch
        {
            return new(false, ServerUnavailableMessage());
        }
    }

    // ---- Google / Apple sign-in ------------------------------------------------------------------------------------
    // The platform gives an ID token (CarScanner.Services.ExternalSignIn); our server verifies it itself, finds or creates
    // the account and answers with the same app session a password login gets. Nothing the provider sent is stored here:
    // only the app session token, in SecureStorage, exactly as for e-mail sign-in.

    /// <summary>
    /// Signs in (or creates the account) with a Google / Apple ID token. <see cref="ExternalSignInStatus.DeviceBound"/> means
    /// the account is on another phone: ask the person, then call again with <paramref name="moveDevice"/> = true and the
    /// same credential. <see cref="ExternalSignInStatus.LinkRequired"/> means an e-mail/password account with the same
    /// address exists and must be proven before the two are joined (<see cref="LinkProviderAsync"/>).
    /// </summary>
    public Task<ExternalSignInResult> SignInWithProviderAsync(ProviderCredential credential, bool moveDevice = false, CancellationToken token = default) =>
        PostExternalAsync(
            "/api/auth/" + credential.Provider,
            new { idToken = credential.IdToken, nonce = credential.Nonce, moveDevice },
            token);

    /// <summary>Joins the provider sign-in from a <see cref="ExternalSignInStatus.LinkRequired"/> answer to the existing account: password first, then the e-mailed code when asked.</summary>
    public Task<ExternalSignInResult> LinkProviderAsync(string linkTicket, string password, string? code, bool moveDevice = false, CancellationToken token = default) =>
        PostExternalAsync(
            "/api/auth/link",
            new { ticket = linkTicket, password, code = string.IsNullOrWhiteSpace(code) ? null : code.Trim(), moveDevice },
            token);

    private async Task<ExternalSignInResult> PostExternalAsync(string path, object body, CancellationToken token)
    {
        EmailVerificationState = null;
        try
        {
            using var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, path);
            request.Content = JsonContent.Create(body);

            using var response = await _http.SendAsync(request, token);
            var text = await ReadBodyAsync(response, token);

            if (response.IsSuccessStatusCode)
            {
                var payload = DeserializeBody<AccountResponse>(text);
                switch (payload?.Status)
                {
                    case "link_required" when !string.IsNullOrWhiteSpace(payload.LinkTicket):
                        return new(ExternalSignInStatus.LinkRequired, string.Empty, LinkTicket: payload.LinkTicket, Email: payload.Email);
                    case "needs_code":
                        return new(ExternalSignInStatus.NeedsCode, string.Empty);
                    case "signed_in" when Guid.TryParse(payload.AccountId, out _) && !string.IsNullOrWhiteSpace(payload.AccessToken):
                        var applied = await ApplySessionAsync(payload, payload.Email ?? string.Empty, markEmailVerificationState: false);
                        return new(ExternalSignInStatus.SignedIn, applied.Message, IsNewAccount: payload.IsNewAccount == true);
                    default:
                        return new(ExternalSignInStatus.Failed, ExternalFailureMessage(null));
                }
            }

            var code = ReadJsonText(text, "code");
            var reason = ReadJsonText(text, "reason");
            var statusCode = (int)response.StatusCode;

            if (response.StatusCode == HttpStatusCode.Conflict && string.Equals(code, DeviceAlreadyBoundCode, StringComparison.OrdinalIgnoreCase))
                return new(ExternalSignInStatus.DeviceBound, string.Empty, DeviceAlreadyBoundCode);
            if (response.StatusCode == HttpStatusCode.Conflict && string.Equals(code, "IDENTITY_ALREADY_LINKED", StringComparison.OrdinalIgnoreCase))
                return new(ExternalSignInStatus.Failed, L("ეს შესვლის გზა უკვე სხვა OBD360 ანგარიშზეა მიბმული.", "This sign-in is already linked to another OBD360 account."), code);
            if (string.Equals(code, RebindCooldownCode, StringComparison.OrdinalIgnoreCase))
                return new(ExternalSignInStatus.Failed, DescribeFailure(response.StatusCode, code, null, isRegister: false, moveDevice: false).Message, RebindCooldownCode);
            if (statusCode == 429)
                return new(ExternalSignInStatus.Failed, L("ძალიან ბევრი მცდელობაა. დაელოდე რამდენიმე წუთს და სცადე თავიდან.", "Too many attempts. Wait a few minutes and try again."), "rate_limited");
            if (statusCode == 503)
                return new(ExternalSignInStatus.Failed, L("ამ გზით შესვლა სერვერზე ჯერ არ არის ჩართული.", "Signing in this way is not switched on yet."), "not_configured");

            return new(ExternalSignInStatus.Failed, ExternalFailureMessage(reason), reason);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(ExternalSignInStatus.Failed, L("შეჩერდა.", "Stopped."), "cancelled");
        }
        catch
        {
            // Network error, timeout, no secure backend. Never include the exception text: it could carry the request.
            return new(ExternalSignInStatus.Failed, ServerUnavailableMessage(), "network");
        }
    }

    private static string ExternalFailureMessage(string? reason) => reason switch
    {
        "email_missing" => L("ამ ანგარიშიდან ელ-ფოსტა არ მივიღეთ, ამიტომ ანგარიშს ვერ შევქმნით. შედი სხვა გზით.",
                             "We did not receive an e-mail address from this account, so we cannot create your account. Sign in another way."),
        "email_not_verified" => L("ამ ანგარიშის ელ-ფოსტა დადასტურებული არ არის, ამიტომ შესასვლელად ვერ გამოვიყენებთ.",
                                  "The e-mail address of this account is not verified, so it cannot be used to sign in."),
        "credentials" => L("ელ-ფოსტა ან პაროლი არასწორია.", "Wrong e-mail or password."),
        "code" => L("კოდი არასწორია. სცადე თავიდან.", "That code is not correct. Try again."),
        "ticket" => L("დაკავშირების მოთხოვნას ვადა გაუვიდა. დაიწყე თავიდან.", "The linking request has expired. Start again."),
        "invalid_token" or "retry" => L("შესვლის დადასტურება ვერ მოხერხდა. სცადე თავიდან.", "The sign-in could not be verified. Try again."),
        _ => L("შესვლა ვერ მოხერხდა. სცადე მოგვიანებით.", "Could not sign in. Try again later.")
    };

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(token);
            return body.Length > 16_384 ? string.Empty : body;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static T? DeserializeBody<T>(string text) where T : class
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try { return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return null; }
    }

    private static string? ReadJsonText(string text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>
    /// "Forgot password", step 1: always reports the same success either way, whether or not the e-mail is
    /// registered — the server never confirms which, and this must not either. A code is emailed only when the
    /// account actually exists.
    /// </summary>
    public async Task<AccountActionResult> ForgotPasswordAsync(string email, CancellationToken token = default)
    {
        try
        {
            using var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/accounts/password/forgot");
            request.Content = JsonContent.Create(new { email = email.Trim() });

            using var response = await _http.SendAsync(request, token);

            return response.IsSuccessStatusCode
                ? new(true, L("თუ ეს ანგარიში არსებობს, კოდი გამოიგზავნა ელფოსტაზე.",
                    "If an account exists for that address, a code was emailed to it."))
                : new(false, ServerUnavailableMessage());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(false, L("შეჩერდა.", "Stopped."));
        }
        catch
        {
            return new(false, ServerUnavailableMessage());
        }
    }

    /// <summary>"Forgot password", step 2: the code just emailed, plus the new password. On success every other session ends.</summary>
    public async Task<AccountActionResult> ResetPasswordAsync(string email, string code, string newPassword, CancellationToken token = default)
    {
        try
        {
            using var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/accounts/password/reset");
            request.Content = JsonContent.Create(new
            {
                email = email.Trim(),
                code = (code ?? string.Empty).Trim(),
                newPassword
            });

            using var response = await _http.SendAsync(request, token);

            if (response.IsSuccessStatusCode)
            {
                return new(true, L("პაროლი განახლდა — შედი ახალი პაროლით.",
                    "Your password was changed — sign in with the new password."));
            }

            var (code2, _) = await ReadErrorAsync(response, token);
            return code2 switch
            {
                "EXPIRED" => new(false, L("კოდს ვადა გაუვიდა. მოითხოვე ახალი.", "The code has expired. Request a new one."), code2),
                "TOO_MANY_ATTEMPTS" => new(false, L("ძალიან ბევრი მცდელობა. მოითხოვე ახალი კოდი.", "Too many attempts. Request a new code."), code2),
                "NO_CODE" => new(false, L("აქტიური კოდი არ არის. მოითხოვე ახალი.", "There is no active code. Request a new one."), code2),
                "WEAK_PASSWORD" => new(false, L("პაროლი უნდა შეიცავდეს 8-დან 128 სიმბოლომდე.", "The password must be 8 to 128 characters."), code2),
                _ => new(false, L("კოდი არასწორია. სცადე თავიდან.", "That code is not correct. Try again."), code2 ?? "INVALID_CODE")
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(false, L("შეჩერდა.", "Stopped."));
        }
        catch
        {
            return new(false, ServerUnavailableMessage());
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
                    ? L("ანგარიშის ამ ტელეფონზე გადმოტანა ახლა ვერ მოხერხდა. სცადე მოგვიანებით ან მოგვწერე: obd360support@gmail.com",
                        "The account could not be moved to this phone right now. Try again later or write to obd360support@gmail.com")
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
                    L("პაროლი 8-დან 128 სიმბოლომდე უნდა იყოს.",
                      "The password must have 8 to 128 characters."));
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
                    L("შეამოწმე ელ-ფოსტა და პაროლი (პაროლი — მინიმუმ 8 სიმბოლო).",
                      text.Trim()));
            }

            return new(
                false,
                isRegister
                    ? L("ანგარიში ვერ შეიქმნა. შეამოწმე ელ-ფოსტა და პაროლი (მინიმუმ 8 სიმბოლო).",
                        "The account could not be created. Check the e-mail and the password (at least 8 characters).")
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
                L("ანგარიში ახლახან გადავიდა სხვა ტელეფონზე. ანგარიშის გადმოტანა შესაძლებელია 7 დღეში ერთხელ — სცადე მოგვიანებით ან მოგვწერე: obd360support@gmail.com",
                  "The account was moved to another phone recently. An account can be moved once per 7 days — try again later or write to obd360support@gmail.com"),
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

        /// <summary>"queued" when the server started a verification email, "unavailable" when it cannot send one.</summary>
        public string? EmailVerification { get; set; }   // "sent", "failed" or "unavailable"

        /// <summary>Login only: true when a sign-in code was just emailed and no session was created yet.</summary>
        public bool? NeedsCode { get; set; }

        // Google / Apple sign-in answers: "signed_in", "link_required" or "needs_code".
        public string? Status { get; set; }

        public string? LinkTicket { get; set; }

        public bool? IsNewAccount { get; set; }
    }

    private sealed class MeResponse
    {
        public bool? IsAdmin { get; set; }
    }

    private sealed class EmailStatusResponse
    {
        public string? Status { get; set; }

        public bool? Verified { get; set; }

        public string? Reason { get; set; }

        public bool? CanSend { get; set; }

        public bool? Required { get; set; }
    }

    private sealed class ChangePasswordResponse
    {
        public string? AccessToken { get; set; }
    }
}

public sealed record EmailVerificationStatus(bool Verified, bool CanSend, bool Required);

public sealed record AccountActionResult(
    bool Success,
    string Message,
    string? Code = null);

public enum ExternalSignInStatus
{
    SignedIn,
    /// <summary>An e-mail/password account with this address exists: the person must prove it is theirs before the sign-ins are joined.</summary>
    LinkRequired,
    /// <summary>A code was e-mailed to the existing account; send it with the same link request.</summary>
    NeedsCode,
    /// <summary>The account is on another phone; the person may move it here.</summary>
    DeviceBound,
    Failed
}

public sealed record ExternalSignInResult(
    ExternalSignInStatus Status,
    string Message,
    string? Code = null,
    string? LinkTicket = null,
    string? Email = null,
    bool IsNewAccount = false);