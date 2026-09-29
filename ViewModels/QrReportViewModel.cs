using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

public partial class QrReportViewModel : BaseViewModel
{
    private readonly QrReportService _reports;
    private readonly QrReportSession _session;
    private readonly LocalizationService _localization;
    private CancellationTokenSource? _cancellation;

    [ObservableProperty] private bool hasQr;
    [ObservableProperty] private string heading = string.Empty;
    [ObservableProperty] private string subtitle = string.Empty;
    [ObservableProperty] private string qrImageUrl = string.Empty;
    [ObservableProperty] private string publicUrl = string.Empty;
    [ObservableProperty] private string expiryText = string.Empty;
    [ObservableProperty] private string createButtonText = string.Empty;
    [ObservableProperty] private string shareButtonText = string.Empty;
    [ObservableProperty] private string openButtonText = string.Empty;
    [ObservableProperty] private string privacyText = string.Empty;

    partial void OnHasQrChanged(bool value) => UpdateTexts();

    public QrReportViewModel(QrReportService reports, QrReportSession session, LocalizationService localization)
    {
        _reports = reports;
        _session = session;
        _localization = localization;
        UpdateTexts();
        _localization.LanguageChanged += (_, _) => UpdateTexts();
    }

    [RelayCommand]
    public async Task Create()
    {
        if (IsBusy || _session.Draft is null) return;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        try
        {
            IsBusy = true;
            HasQr = false;
            StatusMessage = _localization.IsEnglish ? "Creating a secure 30-day link…" : "იქმნება უსაფრთხო 30-დღიანი ბმული…";
            var result = await _reports.CreateAsync(_session.Draft, _cancellation.Token);
            PublicUrl = result.Url;
            QrImageUrl = result.QrImageUrl;
            ExpiryText = _localization.IsEnglish
                ? $"Valid until {result.ExpiresAt.ToLocalTime():dd.MM.yyyy HH:mm}"
                : $"მოქმედებს {result.ExpiresAt.ToLocalTime():dd.MM.yyyy HH:mm}-მდე";
            HasQr = result.Success && !string.IsNullOrWhiteSpace(result.Url);
            StatusMessage = HasQr
                ? (_localization.IsEnglish ? "The buyer can scan this QR with a phone camera." : "მყიდველს შეუძლია QR ტელეფონის კამერით დაასკანეროს.")
                : (_localization.IsEnglish ? "The QR report could not be created." : "QR ანგარიში ვერ შეიქმნა.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusMessage = _localization.IsEnglish
                ? "Could not create the QR report. Check the internet connection and try again."
                : "QR ანგარიში ვერ შეიქმნა. შეამოწმე ინტერნეტი და სცადე თავიდან.";
            System.Diagnostics.Debug.WriteLine($"QR report creation failed: {ex}");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task Share()
    {
        if (!HasQr) return;
        await Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = "GCar360 — QR Report",
            Text = PublicUrl
        });
    }

    [RelayCommand]
    private async Task Open()
    {
        if (HasQr && Uri.TryCreate(PublicUrl, UriKind.Absolute, out var uri))
            await Launcher.Default.OpenAsync(uri);
    }

    public void Cancel() => _cancellation?.Cancel();

    private void UpdateTexts()
    {
        if (_localization.IsEnglish)
        {
            Title = "QR report"; Heading = "QR REPORT";
            Subtitle = "The buyer opens the inspection report in a browser — no app required.";
            CreateButtonText = HasQr ? "CREATE A NEW LINK" : "CREATE QR REPORT";
            ShareButtonText = "SHARE LINK"; OpenButtonText = "OPEN REPORT";
            PrivacyText = "The link expires automatically after 30 days. The full VIN is not shown publicly.";
        }
        else
        {
            Title = "QR ანგარიში"; Heading = "QR ანგარიში";
            Subtitle = "მყიდველი ანგარიშს ბრაუზერში გახსნის — აპის დაყენება არ სჭირდება.";
            CreateButtonText = HasQr ? "ახალი ბმულის შექმნა" : "QR ანგარიშის შექმნა";
            ShareButtonText = "ბმულის გაზიარება"; OpenButtonText = "ანგარიშის გახსნა";
            PrivacyText = "ბმულს 30 დღის შემდეგ ვადა ავტომატურად გასდის. სრული VIN საჯაროდ არ გამოჩნდება.";
        }
    }
}
