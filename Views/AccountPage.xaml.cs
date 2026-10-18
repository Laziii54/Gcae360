using CarScanner.Services;
using CarScanner.Services.Localization;

namespace CarScanner.Views;

/// <summary>
/// OBD360 account: sign in or create an account (e-mail + password).
/// PRO plans and opened VIN histories are kept on the account.
/// </summary>
public partial class AccountPage : ContentPage
{
    private readonly AccountService _account;
    private readonly ProService _pro;
    private readonly LocalizationService _localization;
    private readonly AvatarService _avatars;
    private bool _busy;
    private bool _hasAvatar;

    public AccountPage(AccountService account, ProService pro, LocalizationService localization, AvatarService avatars)
    {
        InitializeComponent();
        _account = account;
        _pro = pro;
        _localization = localization;
        _avatars = avatars;
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        TitleLabel.Text = L("OBD360 ანგარიში", "OBD360 account");
        SubtitleLabel.Text = L(
            "შედი ანგარიშზე — PRO გეგმა და გახსნილი VIN ისტორიები შენს ანგარიშზე ინახება.",
            "Sign in — your PRO plan and opened VIN histories are kept on your account.");
        SignedInAsLabel.Text = L("შესული ხარ როგორც", "Signed in as");
        AvatarHintLabel.Text = L("ფოტოს შეცვლა", "Change photo");
        LogoutButton.Text = L("გამოსვლა", "Sign out");
        EmailLabel.Text = L("ელ-ფოსტა", "E-mail");
        PasswordLabel.Text = L("პაროლი", "Password");
        PasswordHintLabel.Text = L("ახალი ანგარიშისთვის: პაროლი — მინიმუმ 8 სიმბოლო", "New account: password at least 8 characters");
        LoginButton.Text = L("შესვლა", "Sign in");
        RegisterButton.Text = L("ახალი ანგარიშის შექმნა", "Create a new account");
        ForgotPasswordButton.Text = L("დაგავიწყდა პაროლი?", "Forgot password?");
        DeleteHintLabel.Text = L(
            "ანგარიშის წაშლით სერვერიდან სამუდამოდ წაიშლება ანგარიში, PRO სტატუსი და გახსნილი VIN ისტორიები.",
            "Deleting the account permanently removes the account, its PRO status and opened VIN histories from our server.");
        DeleteAccountButton.Text = L("ანგარიშის წაშლა", "Delete account");
        ChangePasswordButton.Text = L("პაროლის შეცვლა", "Change password");
        CurrentPasswordLabel.Text = L("მიმდინარე პაროლი", "Current password");
        NewPasswordLabel.Text = L("ახალი პაროლი", "New password");
        RepeatPasswordLabel.Text = L("გაიმეორე ახალი პაროლი", "Repeat the new password");
        NewPasswordHintLabel.Text = L("მინიმუმ 8 სიმბოლო", "At least 8 characters");
        SavePasswordButton.Text = L("პაროლის შენახვა", "Save password");
        CancelPasswordButton.Text = L("გაუქმება", "Cancel");
        ChangePasswordHintLabel.Text = L(
            "შენ შესული ხარ დროებითი პაროლით. გთხოვთ, დააყენო ახალი პაროლი.",
            "You signed in with a temporary password. Please set a new password.");
        StatusLabel.Text = "";
        UpdateState();
    }

    private void UpdateState()
    {
        var signedIn = _account.IsSignedIn;
        SignedInCard.IsVisible = signedIn;
        DeleteCard.IsVisible = signedIn;
        ChangePasswordCard.IsVisible = signedIn;
        FormCard.IsVisible = !signedIn;
        EmailValueLabel.Text = _account.Email;
        AccountAvatar.Name = _account.Email;
        if (signedIn)
        {
            _ = RefreshEmailStatusAsync();
            _ = RefreshAvatarAsync();
        }
        else
            VerifyCard.IsVisible = false;

        var mustChange = signedIn && _account.MustChangePassword;
        ChangePasswordHintLabel.IsVisible = mustChange;
        if (!signedIn)
        {
            ShowChangePasswordForm(false);
        }
        else if (mustChange)
        {
            ShowChangePasswordForm(true);
        }
    }

    // ---- profile photo ---------------------------------------------------------------------------------------------

    private async Task RefreshAvatarAsync()
    {
        try
        {
            var info = await _avatars.GetMineAsync();
            if (info is null)
                return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _hasAvatar = info.HasAvatar;
                AccountAvatar.AvatarUrl = string.Equals(info.Status, "Approved", StringComparison.OrdinalIgnoreCase) ? info.AvatarUrl ?? string.Empty : string.Empty;
                AvatarHintLabel.Text = string.Equals(info.Status, "Pending", StringComparison.OrdinalIgnoreCase)
                    ? L("ფოტო შენახულია და დადასტურებას ელოდება", "Photo saved, waiting for approval")
                    : L("ფოტოს შეცვლა", "Change photo");
            });
        }
        catch (Exception exception)
        {
            CarScanner.Services.CommandErrors.Report(exception, "AccountPage.RefreshAvatarAsync");
        }
    }

    private async void OnAvatarTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_busy || !_account.IsSignedIn)
                return;

            var choose = L("აირჩიე გალერეიდან", "Choose from gallery");
            var remove = L("ფოტოს წაშლა", "Remove photo");
            var cancel = L("გაუქმება", "Cancel");
            var picked = await this.DisplayActionSheetLocalizedAsync(L("პროფილის ფოტო", "Profile photo"), cancel, _hasAvatar ? remove : null, choose);

            if (picked == choose)
                await PickAndUploadAvatarAsync();
            else if (picked == remove && _hasAvatar)
                await RemoveAvatarAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnAvatarTapped");
        }
    }

    private async Task PickAndUploadAvatarAsync()
    {
        _busy = true;
        try
        {
            byte[]? jpeg;
            try
            {
                jpeg = await _avatars.PickAndShrinkAsync();
            }
            catch (Exception pickException) when (pickException is not OperationCanceledException)
            {
                CarScanner.Services.CommandErrors.Report(pickException, "AccountPage.PickAvatar");
                ShowStatus(L("გალერეის გახსნა ვერ მოხერხდა.", "Could not open the gallery."), error: true);
                return;
            }

            if (jpeg is null)
                return;

            ShowStatus(L("ფოტო იტვირთება…", "Uploading photo…"), error: false);
            var result = await _avatars.UploadAsync(jpeg);
            switch (result.Outcome)
            {
                case AvatarOutcome.Added:
                    _hasAvatar = true;
                    AccountAvatar.AvatarUrl = result.AvatarUrl ?? string.Empty;
                    ShowStatus(L("ფოტო დაემატა.", "Photo added."), error: false);
                    break;
                case AvatarOutcome.WaitingForApproval:
                    _hasAvatar = true;
                    AvatarHintLabel.Text = L("ფოტო შენახულია და დადასტურებას ელოდება", "Photo saved, waiting for approval");
                    ShowStatus(L("ფოტო შენახულია. ის სხვებს დადასტურების შემდეგ დაუჩანს.", "Photo saved. Others will see it after it is approved."), error: false);
                    break;
                case AvatarOutcome.Rejected:
                    ShowStatus(L("ეს ფოტო ვერ გამოვიყენებთ. აირჩიე სხვა.", "This photo can't be used. Choose another one."), error: true);
                    break;
                case AvatarOutcome.TooManyAttempts:
                    ShowStatus(L("ძალიან ბევრი ცდა. სცადე მოგვიანებით.", "Too many attempts. Try again later."), error: true);
                    break;
                case AvatarOutcome.NotSignedIn:
                    ShowStatus(L("ფოტოს დასამატებლად შედი ანგარიშზე.", "Sign in to add a photo."), error: true);
                    break;
                default:
                    ShowStatus(L("ფოტოს ატვირთვა ვერ მოხერხდა. სცადე მოგვიანებით.", "Photo upload failed. Try again later."), error: true);
                    break;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RemoveAvatarAsync()
    {
        _busy = true;
        try
        {
            if (await _avatars.DeleteAsync())
            {
                _hasAvatar = false;
                AccountAvatar.AvatarUrl = string.Empty;
                AvatarHintLabel.Text = L("ფოტოს შეცვლა", "Change photo");
                ShowStatus(L("ფოტო წაიშალა.", "Photo removed."), error: false);
            }
            else
            {
                ShowStatus(L("ფოტოს წაშლა ვერ მოხერხდა.", "Could not remove the photo."), error: true);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowChangePasswordForm(bool show)
    {
        ChangePasswordForm.IsVisible = show;
        ChangePasswordButton.IsVisible = !show;
        // A temporary password must be replaced: no "Cancel" in that case.
        CancelPasswordButton.IsVisible = !_account.MustChangePassword;
        if (!show)
        {
            CurrentPasswordEntry.Text = "";
            NewPasswordEntry.Text = "";
            RepeatPasswordEntry.Text = "";
        }
    }

    private void OnChangePasswordClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_busy || !_account.IsSignedIn)
                return;
            StatusLabel.Text = "";
            ShowChangePasswordForm(true);
            CurrentPasswordEntry.Focus();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnChangePasswordClicked");
        }
    }

    private void OnCancelPasswordClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_busy)
                return;
            ShowChangePasswordForm(false);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnCancelPasswordClicked");
        }
    }

    private async void OnSavePasswordClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_busy || !_account.IsSignedIn)
                return;

            var current = CurrentPasswordEntry.Text ?? "";
            var next = NewPasswordEntry.Text ?? "";
            var repeat = RepeatPasswordEntry.Text ?? "";

            if (current.Length == 0)
            {
                ShowStatus(L("შეიყვანე მიმდინარე პაროლი.", "Enter the current password."), error: true);
                return;
            }

            if (next.Length < 8)
            {
                ShowStatus(L("ახალი პაროლი მინიმუმ 8 სიმბოლო უნდა იყოს.", "The new password must have at least 8 characters."), error: true);
                return;
            }

            if (!string.Equals(next, repeat, StringComparison.Ordinal))
            {
                ShowStatus(L("ახალი პაროლები არ ემთხვევა.", "The new passwords do not match."), error: true);
                return;
            }

            if (string.Equals(next, current, StringComparison.Ordinal))
            {
                ShowStatus(L("ახალი პაროლი ძველისგან უნდა განსხვავდებოდეს.", "The new password must be different from the current one."), error: true);
                return;
            }

            SetBusy(true);
            try
            {
                var result = await _account.ChangePasswordAsync(current, next);
                if (!result.Success)
                {
                    ShowStatus(LocalizationService.T(result.Message), error: true);
                    return;
                }

                ShowChangePasswordForm(false);
                UpdateState();
                ShowStatus(LocalizationService.T(result.Message), error: false);
            }
            catch (Exception ex)
            {
                ShowStatus(UserError.Describe(ex), error: true);
            }
            finally
            {
                SetBusy(false);
            }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnSavePasswordClicked");
        }
    }

    private Task OnLoginAsync() => AuthenticateAsync(register: false);

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        try
        {
            await OnLoginAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnLoginClicked");
        }
    }

    private async void OnRegisterClicked(object? sender, EventArgs e)
    {
        try
        {
            await AuthenticateAsync(register: true);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnRegisterClicked");
        }
    }

    /// <summary>One English text translated into the app language; Georgian is written out.</summary>
    private string LT(string ka, string en) => _localization.IsEnglish ? LocalizationService.T(en) : ka;

    /// <summary>True when there was nothing to verify, or the user entered the code. False means they must not be let in.</summary>
    private async Task<bool> HandleRegistrationEmailAsync()
    {
        switch (_account.EmailVerificationState)
        {
            case "sent":
                return await RunVerificationAsync(EmailVerificationEntry.CodeAlreadySent);
            case "failed":
                // The mail server did not accept the message: say so and offer to try again.
                return await RunVerificationAsync(EmailVerificationEntry.FirstSendFailed);
            default:
                // "unavailable" (the server cannot send email) or an older server: nothing to ask for.
                return true;
        }
    }

    private async Task<bool> RunVerificationAsync(EmailVerificationEntry entry)
    {
        var verified = await EmailVerificationFlow.RunAsync(_account, entry);
        if (verified)
            ShowStatus("" + LT("ელფოსტა დადასტურებულია.", "Email verified."), error: false);
        await RefreshEmailStatusAsync();
        return verified;
    }

    private async void OnVerifyEmailClicked(object? sender, EventArgs e)
    {
        try { await RunVerificationAsync(EmailVerificationEntry.SendFirst); }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnVerifyEmailClicked");
        }
    }

    /// <summary>Shows whether the signed-in account's email is verified. Hidden when the server cannot be asked.</summary>
    private async Task RefreshEmailStatusAsync()
    {
        try
        {
            if (!_account.IsSignedIn)
            {
                VerifyCard.IsVisible = false;
                return;
            }

            var status = await _account.GetEmailStatusAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (status is null || (!status.Verified && !status.CanSend))
                {
                    VerifyCard.IsVisible = false;      // unknown, or this server cannot send email: nothing to offer
                    return;
                }

                VerifyCard.IsVisible = true;
                VerifyStatusLabel.Text = status.Verified
                    ? "" + LT("ელფოსტა დადასტურებულია.", "Email verified.")
                    : LT("ელფოსტა ჯერ არ არის დადასტურებული.", "Email not verified yet.");
                VerifyEmailButton.Text = LT("დაადასტურე ელფოსტა", "Verify your email");
                VerifyEmailButton.IsVisible = !status.Verified;
            });
        }
        catch
        {
            VerifyCard.IsVisible = false;
        }
    }

    private async Task AuthenticateAsync(bool register)
    {
        if (_busy)
            return;

        var email = (EmailEntry.Text ?? "").Trim();
        var password = PasswordEntry.Text ?? "";
        if (register && !EmailAddressRules.IsValidForRegistration(email))
        {
            ShowStatus(LT("შეიყვანე სწორი ელფოსტის მისამართი.", "Enter a valid email address."), error: true);
            return;
        }

        if (register && password.Length < 8)
        {
            ShowStatus(L("პაროლი მინიმუმ 8 სიმბოლო უნდა იყოს.", "The password must have at least 8 characters."), error: true);
            return;
        }

        SetBusy(true);
        try
        {
            var result = register
                ? await _account.RegisterAsync(email, password)
                : await _account.LoginAsync(email, password);

            var moveDevice = false;

            // The account is linked to another phone: ask once, then move it here.
            if (!register &&
                !result.Success &&
                result.Code == AccountService.DeviceAlreadyBoundCode)
            {
                var move = await this.DisplayAlertLocalizedAsync(
                    L("ანგარიში სხვა ტელეფონზეა", "Account is on another phone"),
                    L("ეს ანგარიში სხვა ტელეფონზეა მიბმული. გადმოვიტანოთ ამ ტელეფონზე? ამის შემდეგ ანგარიში ამ ტელეფონზე იმუშავებს ძველის ნაცვლად.",
                      "This account is linked to another phone. Move it to this phone? It will then work on this phone instead of the other one."),
                    L("გადმოტანა", "Move here"),
                    L("გაუქმება", "Cancel"));

                if (!move)
                {
                    ShowStatus(LocalizationService.T(result.Message), error: true);
                    return;
                }

                moveDevice = true;
                result = await _account.LoginAsync(email, password, moveDevice: true);
            }

            // Login only: a sign-in code was just emailed — every login needs it, not only the first one.
            if (!register &&
                !result.Success &&
                result.Code == AccountService.NeedsLoginCodeCode)
            {
                result = await RunLoginCodeAsync(email, password, moveDevice);
            }

            if (!result.Success)
            {
                ShowStatus(LocalizationService.T(result.Message), error: true);
                return;
            }

            // Admin / support set a temporary password: the user must choose a
            // new one now (current password is the one just typed).
            var mustChange = !register && _account.MustChangePassword;
            if (mustChange)
                CurrentPasswordEntry.Text = password;

            PasswordEntry.Text = "";
            // PRO status of the account (also admin / promo plans).
            try { await _pro.LoadAsync(forceServerRefresh: true); } catch { }

            ShowStatus(L("შესული ხარ", "Signed in"), error: false);
            UpdateState();

            if (mustChange)
            {
                await this.DisplayAlertLocalizedAsync(
                    L("დააყენე ახალი პაროლი", "Set a new password"),
                    L("შენ შესული ხარ დროებითი პაროლით. უსაფრთხოებისთვის ახლავე დააყენე ახალი პაროლი (მინიმუმ 8 სიმბოლო).",
                      "You signed in with a temporary password. For your security, set a new password now (at least 8 characters)."),
                    "OK");
                NewPasswordEntry.Focus();
                return;
            }

            // Registration: the account now exists and this device just signed in, but nobody gets into the app
            // on a fresh registration without entering the code — there is no "skip" any more. Declining or
            // failing to verify signs the brand-new session back out; the account itself is untouched, and the
            // user can sign in again (and verify) once they have the code.
            if (register)
            {
                var verified = await HandleRegistrationEmailAsync();
                if (!verified)
                {
                    await _account.LogoutAsync();
                    UpdateState();
                    ShowStatus(
                        LT("ანგარიში შეიქმნა. შესასვლელად ჯერ დაადასტურე ელფოსტა — შედი ხელახლა, როცა კოდი გექნება.",
                           "Account created. Verify your email to sign in — log in again once you have the code."),
                        error: true);
                    return;
                }
            }

            await Task.Delay(700);
            try { await CarScanner.Services.SafeNavigation.GoToAsync(".."); } catch { }
        }
        catch (Exception ex)
        {
            ShowStatus(UserError.Describe(ex), error: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Second step of login: the code just emailed to the account. There is no skip option — if this is not
    /// completed, the caller's result stays unsuccessful and no session was ever created (the server never issued
    /// a token for this attempt), so there is nothing to sign back out of.
    /// </summary>
    private async Task<AccountActionResult> RunLoginCodeAsync(string email, string password, bool moveDevice)
    {
        var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return new(false, L("შესვლა ვერ მოხერხდა.", "Could not sign in."));

        for (var round = 0; round < 6; round++)
        {
            var code = await page.DisplayPromptLocalizedAsync(
                LT("შესვლის დადასტურება", "Confirm sign-in"),
                LT("უსაფრთხოებისთვის ყოველ შესვლაზე 6-ნიშნა კოდს გიგზავნით ელფოსტაზე. შეიყვანე აქ გასაგრძელებლად.",
                   "For security, we email a 6-digit code on every sign-in. Enter it here to continue."),
                LT("დადასტურება", "Confirm"),
                // DisplayPromptAsync's "cancel" is not documented as nullable (unlike DisplayAlertAsync's) and
                // crashes on iOS when null is passed — a real label is required here; code is null below already
                // treats a dismissed prompt as "not verified", so this does not add a way to skip verification.
                cancel: LT("გაუქმება", "Cancel"),
                placeholder: "123456",
                maxLength: 6,
                keyboard: Keyboard.Numeric);

            if (code is null)
                return new(false, LT("შესვლა გაუქმდა.", "Sign-in cancelled."));

            var result = await _account.VerifyLoginCodeAsync(email, password, code, moveDevice);
            if (result.Success || result.Code is "expired" or "no_code" or "too_many_attempts")
                return result;

            await page.DisplayAlertLocalizedAsync(
                LT("კოდი არასწორია", "Wrong code"),
                LocalizationService.T(result.Message),
                "OK");
        }

        return new(false, LT("ვერ დადასტურდა. სცადე ხელახლა შესვლა.", "Could not confirm. Try signing in again."));
    }

    /// <summary>
    /// Self-service reset: a code is emailed to the account (same server-side infrastructure as the login 2FA
    /// code, but a separate store so the two never collide), then the code plus a new password are sent together.
    /// </summary>
    private async void OnForgotPasswordClicked(object? sender, EventArgs e)
    {
        if (_busy)
            return;

        try
        {
            SetBusy(true);
            try
            {
                var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page is null)
                    return;

                var email = (EmailEntry.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(email))
                {
                    email = (await page.DisplayPromptLocalizedAsync(
                        L("პაროლის აღდგენა", "Reset password"),
                        L("შეიყვანე ანგარიშის ელ-ფოსტა — კოდს გამოგიგზავნით.", "Enter your account's email — we'll send a code."),
                        L("გაგრძელება", "Continue"),
                        cancel: L("გაუქმება", "Cancel"),
                        keyboard: Keyboard.Email) ?? "").Trim();

                    if (string.IsNullOrWhiteSpace(email))
                        return;
                }

                var sent = await _account.ForgotPasswordAsync(email);
                ShowStatus(sent.Message, error: !sent.Success);
                if (!sent.Success)
                    return;

                for (var round = 0; round < 6; round++)
                {
                    var code = await page.DisplayPromptLocalizedAsync(
                        L("შეიყვანე კოდი", "Enter the code"),
                        L("თუ ეს ანგარიში არსებობს, ელფოსტაზე მიიღებ 6-ნიშნა კოდს. შეიყვანე აქ.",
                          "If that account exists, you'll get a 6-digit code by email. Enter it here."),
                        L("შემდეგი", "Next"),
                        cancel: L("გაუქმება", "Cancel"),
                        placeholder: "123456",
                        maxLength: 6,
                        keyboard: Keyboard.Numeric);

                    if (code is null)
                    {
                        ShowStatus(L("პაროლის აღდგენა გაუქმდა.", "Password reset cancelled."), error: false);
                        return;
                    }

                    string? newPassword;
                    while (true)
                    {
                        newPassword = await page.DisplayPromptLocalizedAsync(
                            L("ახალი პაროლი", "New password"),
                            L("შეიყვანე ახალი პაროლი (მინიმუმ 8 სიმბოლო).", "Enter a new password (at least 8 characters)."),
                            L("პაროლის დაყენება", "Set password"),
                            cancel: L("გაუქმება", "Cancel"));

                        if (newPassword is null)
                        {
                            ShowStatus(L("პაროლის აღდგენა გაუქმდა.", "Password reset cancelled."), error: false);
                            return;
                        }

                        if (newPassword.Length >= 8)
                            break;

                        await page.DisplayAlertLocalizedAsync(
                            L("მოკლე პაროლი", "Password too short"),
                            L("პაროლი უნდა შეიცავდეს მინიმუმ 8 სიმბოლოს.", "The password must be at least 8 characters."),
                            "OK");
                    }

                    var result = await _account.ResetPasswordAsync(email, code, newPassword);
                    if (result.Success)
                    {
                        ShowStatus(result.Message, error: false);
                        EmailEntry.Text = email;
                        PasswordEntry.Text = "";
                        return;
                    }

                    if (result.Code is "EXPIRED" or "TOO_MANY_ATTEMPTS" or "NO_CODE")
                    {
                        // The code itself is gone (burned or expired): looping back would just ask for the
                        // same dead code again — the person has to tap "Forgot password?" again for a new one.
                        ShowStatus(result.Message, error: true);
                        return;
                    }

                    await page.DisplayAlertLocalizedAsync(
                        L("ვერ შესრულდა", "Could not reset"),
                        result.Message,
                        "OK");
                }

                ShowStatus(
                    L("ვერ დადასტურდა. სცადე თავიდან „პაროლის აღდგენა“-დან.", "Could not confirm. Start over from \"Forgot password\"."),
                    error: true);
            }
            finally
            {
                SetBusy(false);
            }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnForgotPasswordClicked");
        }
    }

    private async void OnDeleteAccountClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_busy || !_account.IsSignedIn)
                return;

            var storeNote = DeviceInfo.Current.Platform == DevicePlatform.iOS
                ? L("\n\nApp Store-ით ნაყიდი PRO გამოწერა ავტომატურად არ უქმდება — გააუქმე ის ცალკე: პარამეტრები → შენი სახელი → გამოწერები.",
                    "\n\nA PRO subscription bought through the App Store is not cancelled automatically — cancel it separately in Settings → your name → Subscriptions.")
                : "";
            var first = await this.DisplayAlertLocalizedAsync(
                L("ანგარიშის წაშლა", "Delete account"),
                L("ანგარიში, PRO სტატუსი, გახსნილი VIN ისტორიები და ანგარიშთან დაკავშირებული სერვერის მონაცემები სამუდამოდ წაიშლება. ამის დაბრუნება შეუძლებელია.",
                  "Your account, PRO status, opened VIN histories and the account data on our server will be permanently deleted. This cannot be undone.") + storeNote,
                L("გაგრძელება", "Continue"),
                L("გაუქმება", "Cancel"));
            if (!first)
                return;

            var second = await this.DisplayAlertLocalizedAsync(
                L("ნამდვილად წავშალოთ?", "Delete permanently?"),
                L($"ანგარიში {_account.Email} სამუდამოდ წაიშლება.",
                  $"The account {_account.Email} will be deleted permanently."),
                L("წაშლა", "Delete"),
                L("გაუქმება", "Cancel"));
            if (!second)
                return;

            SetBusy(true);
            try
            {
                var result = await _account.DeleteAccountAsync();
                if (!result.Success)
                {
                    ShowStatus(LocalizationService.T(result.Message), error: true);
                    return;
                }

                // DeleteAccountAsync already removed the local sign-in; make sure
                // the server session is gone too and PRO falls back to FREE.
                if (_account.IsSignedIn)
                    await _account.LogoutAsync();
                try { await _pro.LoadAsync(forceServerRefresh: true); } catch { }

                EmailEntry.Text = "";
                PasswordEntry.Text = "";
                UpdateState();
                ShowStatus(LocalizationService.T(result.Message), error: false);
                await this.DisplayAlertLocalizedAsync(
                    L("ანგარიში წაიშალა", "Account deleted"),
                    LocalizationService.T(result.Message),
                    "OK");
            }
            catch (Exception ex)
            {
                ShowStatus(UserError.Describe(ex), error: true);
            }
            finally
            {
                SetBusy(false);
            }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnDeleteAccountClicked");
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        try
        {
        if (_busy)
            return;
        SetBusy(true);
        try
        {
            await _account.LogoutAsync();
            try { await _pro.LoadAsync(forceServerRefresh: true); } catch { }
            ShowStatus(L("გამოხვედი ანგარიშიდან.", "Signed out."), error: false);
            UpdateState();
        }
        finally
        {
            SetBusy(false);
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnLogoutClicked");
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Busy.IsVisible = busy;
        Busy.IsRunning = busy;
        LoginButton.IsEnabled = !busy;
        RegisterButton.IsEnabled = !busy;
        LogoutButton.IsEnabled = !busy;
        DeleteAccountButton.IsEnabled = !busy;
        ChangePasswordButton.IsEnabled = !busy;
        SavePasswordButton.IsEnabled = !busy;
        CancelPasswordButton.IsEnabled = !busy;
        ForgotPasswordButton.IsEnabled = !busy;
    }

    private void ShowStatus(string text, bool error)
    {
        StatusLabel.Text = text;
        StatusLabel.TextColor = error ? Color.FromArgb("#EF4444") : Color.FromArgb("#22C55E");
    }
}
