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
    private bool _busy;

    public AccountPage(AccountService account, ProService pro, LocalizationService localization)
    {
        InitializeComponent();
        _account = account;
        _pro = pro;
        _localization = localization;
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
        if (signedIn)
            _ = RefreshEmailStatusAsync();
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

    private async Task HandleRegistrationEmailAsync()
    {
        switch (_account.EmailVerificationState)
        {
            case "sent":
                await RunVerificationAsync(EmailVerificationEntry.CodeAlreadySent);
                break;
            case "failed":
                // The mail server did not accept the message: say so and offer to try again.
                await RunVerificationAsync(EmailVerificationEntry.FirstSendFailed);
                break;
            default:
                // "unavailable" (the server cannot send email) or an older server: nothing to ask for.
                break;
        }
    }

    private async Task RunVerificationAsync(EmailVerificationEntry entry)
    {
        var verified = await EmailVerificationFlow.RunAsync(_account, entry);
        if (verified)
            ShowStatus("" + LT("ელფოსტა დადასტურებულია.", "Email verified."), error: false);
        await RefreshEmailStatusAsync();
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

                result = await _account.LoginAsync(email, password, moveDevice: true);
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

            // Registration: what the server said about the verification email decides what happens next. Skipping
            // it ("Later") is fine: scanning and every local feature work without a verified email. Only paid
            // actions (checkout, VIN report purchase) require it, and ask again at that point if needed.
            if (register)
                await HandleRegistrationEmailAsync();

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
    /// The backend cannot send e-mails, so a password reset is requested from
    /// support by e-mail.
    /// </summary>
    private async void OnForgotPasswordClicked(object? sender, EventArgs e)
    {
        try
        {
            var email = (EmailEntry.Text ?? "").Trim();
            var body = string.IsNullOrWhiteSpace(email)
                ? ""
                : L("ჩემი ანგარიშის ელ-ფოსტა: ", "My account e-mail: ") + email;
            var uri = $"mailto:{ContactService.ContactEmail}?subject={Uri.EscapeDataString("Password reset")}" +
                      (body.Length > 0 ? $"&body={Uri.EscapeDataString(body)}" : "");

            var opened = false;
            try { opened = await Launcher.Default.OpenAsync(new Uri(uri)); } catch { }

            if (!opened)
            {
                ShowStatus(
                    L($"პაროლის აღსადგენად მოგვწერე: {ContactService.ContactEmail}",
                      $"To reset your password, write to {ContactService.ContactEmail}"),
                    error: false);
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
