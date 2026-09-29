using CarScanner.Services;
using CarScanner.Services.Localization;

namespace CarScanner.Views;

/// <summary>
/// GCar360 account: sign in or create an account (e-mail + password).
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
        TitleLabel.Text = L("GCar360 ანგარიში", "GCar360 account");
        SubtitleLabel.Text = L(
            "შედი ანგარიშზე — PRO გეგმა და გახსნილი VIN ისტორიები შენს ანგარიშზე ინახება.",
            "Sign in — your PRO plan and opened VIN histories are kept on your account.");
        SignedInAsLabel.Text = L("შესული ხარ როგორც", "Signed in as");
        LogoutButton.Text = L("გამოსვლა", "Sign out");
        EmailLabel.Text = L("ელ-ფოსტა", "E-mail");
        PasswordLabel.Text = L("პაროლი", "Password");
        PasswordHintLabel.Text = L("ახალი ანგარიშისთვის: პაროლი — მინიმუმ 10 სიმბოლო", "New account: password at least 10 characters");
        LoginButton.Text = L("შესვლა", "Sign in");
        RegisterButton.Text = L("ახალი ანგარიშის შექმნა", "Create a new account");
        StatusLabel.Text = "";
        UpdateState();
    }

    private void UpdateState()
    {
        var signedIn = _account.IsSignedIn;
        SignedInCard.IsVisible = signedIn;
        FormCard.IsVisible = !signedIn;
        EmailValueLabel.Text = _account.Email;
    }

    private Task OnLoginAsync() => AuthenticateAsync(register: false);

    private async void OnLoginClicked(object? sender, EventArgs e) => await OnLoginAsync();

    private async void OnRegisterClicked(object? sender, EventArgs e) => await AuthenticateAsync(register: true);

    private async Task AuthenticateAsync(bool register)
    {
        if (_busy)
            return;

        var email = (EmailEntry.Text ?? "").Trim();
        var password = PasswordEntry.Text ?? "";
        if (register && password.Length < 10)
        {
            ShowStatus(L("პაროლი მინიმუმ 10 სიმბოლო უნდა იყოს.", "The password must have at least 10 characters."), error: true);
            return;
        }

        SetBusy(true);
        try
        {
            var result = register
                ? await _account.RegisterAsync(email, password)
                : await _account.LoginAsync(email, password);

            if (!result.Success)
            {
                ShowStatus(LocalizationService.T(result.Message), error: true);
                return;
            }

            PasswordEntry.Text = "";
            // PRO status of the account (also admin / promo plans).
            try { await _pro.LoadAsync(forceServerRefresh: true); } catch { }

            ShowStatus(L("✓ შესული ხარ", "✓ Signed in"), error: false);
            UpdateState();
            await Task.Delay(700);
            try { await Shell.Current.GoToAsync(".."); } catch { }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
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

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Busy.IsVisible = busy;
        Busy.IsRunning = busy;
        LoginButton.IsEnabled = !busy;
        RegisterButton.IsEnabled = !busy;
        LogoutButton.IsEnabled = !busy;
    }

    private void ShowStatus(string text, bool error)
    {
        StatusLabel.Text = text;
        StatusLabel.TextColor = error ? Color.FromArgb("#EF4444") : Color.FromArgb("#22C55E");
    }
}
