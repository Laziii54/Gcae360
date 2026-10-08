using CarScanner.Services;
using CarScanner.Services.Localization;

namespace CarScanner.Views;

public partial class ContactPage : ContentPage
{
    private readonly ContactService _contact;
    private readonly LocalizationService _localization;
    public ContactPage(ContactService contact, LocalizationService localization)
    {
        InitializeComponent();
        _contact = contact;
        _localization = localization;
    }

    private async void OnSendClicked(object sender, EventArgs e)
    {
        try
        {
        var subject = string.IsNullOrWhiteSpace(SubjectEntry.Text) ? "Georgian Car Scanner" : SubjectEntry.Text.Trim();
        var message = MessageEditor.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            await DisplayAlertAsync("Contact", _localization.IsEnglish ? "Write a message first." : "ჯერ ჩაწერე შეტყობინება.", "OK");
            return;
        }
        try
        {
            await _contact.SendAsync(subject, message);
            StatusLabel.Text = _localization.IsEnglish ? "Your email app was opened." : "თქვენი ელფოსტის აპი გაიხსნა.";
            MessageEditor.Text = string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Contact send failed: {ex}");
            StatusLabel.Text = CarScanner.Services.UserError.Describe(ex);
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ContactPage.OnSendClicked");
        }
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();
    }
}
