using CarScanner.Services;
using CarScanner.Services.ExternalSignIn;
using CarScanner.Services.Localization;

namespace CarScanner.Views;

/// <summary>
/// OBD360 account: continue with Google / Apple, or sign in / create an account with e-mail + password.
/// PRO plans and opened VIN histories are kept on the account.
/// </summary>
public partial class AccountPage : ContentPage
{
    private readonly AccountService _account;
    private readonly ProService _pro;
    private readonly LocalizationService _localization;
    private readonly AvatarService _avatars;
    private readonly ExternalSignInService _external;
    private bool _busy;
    private bool _hasAvatar;
    private bool _emailExpanded;

    /// <summary>A Google / Apple sign-in whose e-mail already has an OBD360 password account: waiting for that account's password.</summary>
    private sealed record PendingLink(string Ticket, string Email);
    private PendingLink? _pendingLink;

    public AccountPage(AccountService account, ProService pro, LocalizationService localization, AvatarService avatars, ExternalSignInService external)
    {
        InitializeComponent();
        _account = account;
        _pro = pro;
        _localization = localization;
        _avatars = avatars;
        _external = external;
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
        GoogleButton.Text = L("გაგრძელება Google-ით", "Continue with Google");
        AppleButton.Text = L("გაგრძელება Apple-ით", "Continue with Apple");
        EmailToggleButton.Text = L("გაგრძელება ელ-ფოსტით", "Continue with Email");
        OrLabel.Text = L("ან", "or");
        CancelLinkButton.Text = L("დაკავშირების გაუქმება", "Cancel linking");
        // Screen readers read the same words the button shows.
        SemanticProperties.SetDescription(GoogleButton, GoogleButton.Text);
        SemanticProperties.SetDescription(AppleButton, AppleButton.Text);
        SemanticProperties.SetDescription(EmailToggleButton, EmailToggleButton.Text);
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
        UpdateSignInCards(signedIn);
        EmailValueLabel.Text = _account.Email;
        AccountAvatar.Name = _account.Email;
        // The kept copy of the own picture shows immediately, also offline; the server answer below confirms or clears it.
        AccountAvatar.LocalFilePath = signedIn ? AvatarService.LocalFileFor(_account.Email) : string.Empty;
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
                if (info.HasAvatar)
                {
                    AccountAvatar.LocalFilePath = AvatarService.LocalFileFor(_account.Email);
                }
                else
                {
                    // Removed on the server (another phone, or the check refused it): the kept copy goes too.
                    AvatarService.PruneLocal(_account.Email);
                    AccountAvatar.LocalFilePath = string.Empty;
                }

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

            // The chosen picture appears at once; the upload runs behind it.
            var email = _account.Email;
            var candidate = AvatarService.SaveLocalCandidate(email, jpeg);
            if (candidate.Length > 0)
                AccountAvatar.LocalFilePath = candidate;

            ShowStatus(L("ფოტო იტვირთება…", "Uploading photo…"), error: false);
            var result = await _avatars.UploadAsync(jpeg);
            if (result.Outcome is AvatarOutcome.Added or AvatarOutcome.WaitingForApproval)
            {
                if (candidate.Length > 0)
                    AvatarService.PruneLocal(email, candidate);
            }
            else
            {
                AvatarService.DeleteLocalFile(candidate);
                AccountAvatar.LocalFilePath = AvatarService.LocalFileFor(email);
            }

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
                    _ = WaitForApprovalAsync();
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

    /// <summary>After "saved, waiting": the server finishes its check in the background. Look a few times and show the outcome.</summary>
    private async Task WaitForApprovalAsync()
    {
        try
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (!_account.IsSignedIn)
                    return;

                var info = await _avatars.GetMineAsync();
                if (info is null)
                    continue;

                if (!info.HasAvatar)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        _hasAvatar = false;
                        AvatarService.PruneLocal(_account.Email);
                        AccountAvatar.LocalFilePath = string.Empty;
                        AvatarHintLabel.Text = L("ფოტოს შეცვლა", "Change photo");
                        ShowStatus(L("ეს ფოტო ვერ გამოვიყენეთ. აირჩიე სხვა.", "This photo can't be used. Choose another one."), error: true);
                    });
                    return;
                }

                if (string.Equals(info.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        AccountAvatar.AvatarUrl = info.AvatarUrl ?? string.Empty;
                        AvatarHintLabel.Text = L("ფოტოს შეცვლა", "Change photo");
                        ShowStatus(L("ფოტო დაემატა.", "Photo added."), error: false);
                    });
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            CarScanner.Services.CommandErrors.Report(exception, "AccountPage.WaitForApprovalAsync");
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
                AvatarService.PruneLocal(_account.Email);
                AccountAvatar.LocalFilePath = string.Empty;
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

    // ---- Google / Apple ---------------------------------------------------------------------------------------------

    /// <summary>Which of the provider buttons / the e-mail form are on screen. With no provider available (e.g. a Release build without client ids) the page is the plain e-mail form, as before.</summary>
    private void UpdateSignInCards(bool signedIn)
    {
        var google = _external.ShowGoogle;
        var apple = _external.ShowApple;
        var providers = google || apple;
        var linking = _pendingLink is not null;

        ProviderCard.IsVisible = !signedIn && providers && !linking;
        GoogleButton.IsVisible = google;
        AppleButton.IsVisible = apple;
        ProviderDivider.IsVisible = providers;
        EmailToggleButton.IsVisible = providers && !_emailExpanded;
        FormCard.IsVisible = !signedIn && (_emailExpanded || !providers || linking);

        LinkHintLabel.IsVisible = linking;
        EmailEntry.IsReadOnly = linking;
        RegisterButton.IsVisible = !linking;
        CancelLinkButton.IsVisible = linking;
        LoginButton.Text = linking ? L("დაკავშირება და შესვლა", "Link and sign in") : L("შესვლა", "Sign in");
        if (linking)
        {
            LinkHintLabel.Text = string.Format(
                LT("OBD360 ანგარიში ელ-ფოსტით {0} უკვე არსებობს. ამ შესვლის დასაკავშირებლად შეიყვანე იმ ანგარიშის პაროლი.",
                   "An OBD360 account with {0} already exists. To link this sign-in to it, enter that account's password."),
                _pendingLink!.Email);
        }
    }

    private void OnEmailToggleClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_busy)
                return;
            _emailExpanded = true;
            UpdateState();
            EmailEntry.Focus();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnEmailToggleClicked");
        }
    }

    private async void OnGoogleClicked(object? sender, EventArgs e)
    {
        try { await ProviderSignInAsync(google: true); }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnGoogleClicked");
        }
    }

    private async void OnAppleClicked(object? sender, EventArgs e)
    {
        try { await ProviderSignInAsync(google: false); }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnAppleClicked");
        }
    }

    private void OnCancelLinkClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_busy)
                return;
            _pendingLink = null;
            PasswordEntry.Text = "";
            StatusLabel.Text = "";
            UpdateState();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccountPage.OnCancelLinkClicked");
        }
    }

    /// <summary>
    /// One tap on "Continue with Google / Apple": the provider's own sheet, then our server. A first-time person gets an
    /// account without filling in anything. Closing the sheet is silent. Nothing is signed in unless the server says so.
    /// </summary>
    private async Task ProviderSignInAsync(bool google)
    {
        if (_busy)
            return;

        SetBusy(true);
        try
        {
            StatusLabel.Text = "";
            ProviderCredential? credential;
            try
            {
                credential = google
                    ? await _external.GetGoogleCredentialAsync()
                    : await _external.GetAppleCredentialAsync();
            }
            catch (ExternalSignInException ex)
            {
                ShowStatus(
                    ex.Failure == ExternalSignInFailure.NotConfigured
                        ? (google
                            ? LT("Google-ით შესვლა ამ ვერსიაში ჯერ არ არის მომზადებული.", "Google sign-in is not set up in this version yet.")
                            : LT("Apple-ით შესვლა ამ მოწყობილობაზე მიუწვდომელია.", "Sign in with Apple is not available on this device."))
                        : LT("შესვლა ვერ მოხერხდა. სცადე მოგვიანებით.", "Could not sign in. Try again later."),
                    error: true);
                return;
            }

            if (credential is null)
                return;   // the person closed the sheet: nothing to say

            var result = await _account.SignInWithProviderAsync(credential);

            // The account is on another phone: ask once, then move it here with the same (still unused) credential.
            if (result.Status == ExternalSignInStatus.DeviceBound)
            {
                var move = await this.DisplayAlertLocalizedAsync(
                    L("ანგარიში სხვა ტელეფონზეა", "Account is on another phone"),
                    L("ეს ანგარიში სხვა ტელეფონზეა მიბმული. გადმოვიტანოთ ამ ტელეფონზე? ამის შემდეგ ანგარიში ამ ტელეფონზე იმუშავებს ძველის ნაცვლად.",
                      "This account is linked to another phone. Move it to this phone? It will then work on this phone instead of the other one."),
                    L("გადმოტანა", "Move here"),
                    L("გაუქმება", "Cancel"));
                if (!move)
                    return;

                result = await _account.SignInWithProviderAsync(credential, moveDevice: true);
            }

            await HandleProviderResultAsync(result);
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

    private async Task HandleProviderResultAsync(ExternalSignInResult result)
    {
        switch (result.Status)
        {
            case ExternalSignInStatus.SignedIn:
                await FinishProviderSignInAsync(result.IsNewAccount);
                break;

            case ExternalSignInStatus.LinkRequired:
                // Same e-mail as an existing password account. Never joined on the address alone: the person proves they
                // own that account (its password, then a code e-mailed to it) in the form below.
                _pendingLink = new PendingLink(result.LinkTicket!, result.Email ?? string.Empty);
                EmailEntry.Text = result.Email ?? string.Empty;
                PasswordEntry.Text = "";
                UpdateState();
                PasswordEntry.Focus();
                break;

            case ExternalSignInStatus.DeviceBound:
                // Linked, but the account is on another phone: signing in again (and confirming the move) finishes it.
                _pendingLink = null;
                PasswordEntry.Text = "";
                UpdateState();
                ShowStatus(
                    LT("დაკავშირებულია. ახლა ხელახლა დააჭირე Google/Apple-ით გაგრძელებას და ანგარიში ამ ტელეფონზე გადმოიტანე.",
                       "Linked. Now tap Continue with Google or Apple again to move the account to this phone."),
                    error: false);
                break;

            default:
                ShowStatus(LocalizationService.T(result.Message), error: true);
                break;
        }
    }

    private async Task FinishProviderSignInAsync(bool isNewAccount)
    {
        PasswordEntry.Text = "";
        _pendingLink = null;
        // PRO status of the account (also admin / promo plans). Signing in never grants PRO by itself.
        try { await _pro.LoadAsync(forceServerRefresh: true); } catch { }

        ShowStatus(
            isNewAccount
                ? LT("ანგარიში შეიქმნა. კეთილი იყოს შენი მობრძანება!", "Your account was created. Welcome!")
                : L("შესული ხარ", "Signed in"),
            error: false);
        UpdateState();

        await Task.Delay(700);
        try { await CarScanner.Services.SafeNavigation.GoToAsync(".."); } catch { }
    }

    /// <summary>Password step of joining a Google / Apple sign-in to an existing account, then the e-mailed code when the server asks for it.</summary>
    private async Task LinkPendingAsync()
    {
        if (_busy || _pendingLink is not { } pending)
            return;

        var password = PasswordEntry.Text ?? "";
        if (password.Length == 0)
        {
            ShowStatus(LT("შეიყვანე ანგარიშის პაროლი.", "Enter the account's password."), error: true);
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _account.LinkProviderAsync(pending.Ticket, password, null);
            if (result.Status == ExternalSignInStatus.NeedsCode)
                result = await RunLinkCodeAsync(pending.Ticket, password);

            await HandleProviderResultAsync(result);
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

    private async Task<ExternalSignInResult> RunLinkCodeAsync(string ticket, string password)
    {
        var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return new(ExternalSignInStatus.Failed, LT("შესვლა ვერ მოხერხდა.", "Could not sign in."));

        for (var round = 0; round < 3; round++)
        {
            var code = await page.DisplayPromptLocalizedAsync(
                LT("დაკავშირების დადასტურება", "Confirm linking"),
                LT("უსაფრთხოებისთვის ამ ანგარიშის ელ-ფოსტაზე 6-ნიშნა კოდი გამოგიგზავნეთ. შეიყვანე აქ გასაგრძელებლად.",
                   "For security, we emailed a 6-digit code to this account's address. Enter it here to continue."),
                LT("დადასტურება", "Confirm"),
                cancel: LT("გაუქმება", "Cancel"),
                placeholder: "123456",
                maxLength: 6,
                keyboard: Keyboard.Numeric);

            if (code is null)
                return new(ExternalSignInStatus.Failed, LT("შესვლა გაუქმდა.", "Sign-in cancelled."));

            var result = await _account.LinkProviderAsync(ticket, password, code);
            if (result.Status != ExternalSignInStatus.Failed || result.Code != "code")
                return result;

            await page.DisplayAlertLocalizedAsync(
                LT("კოდი არასწორია", "Wrong code"),
                LocalizationService.T(result.Message),
                "OK");
        }

        return new(ExternalSignInStatus.Failed, LT("ვერ დადასტურდა. სცადე ხელახლა შესვლა.", "Could not confirm. Try signing in again."));
    }

    private Task OnLoginAsync() => _pendingLink is not null ? LinkPendingAsync() : AuthenticateAsync(register: false);

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
        GoogleButton.IsEnabled = !busy;
        AppleButton.IsEnabled = !busy;
        EmailToggleButton.IsEnabled = !busy;
        CancelLinkButton.IsEnabled = !busy;
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
