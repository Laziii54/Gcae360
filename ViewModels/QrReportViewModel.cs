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
        Attach();
    }

    // Detached by the page when it goes away (no leak through singleton services).
    private bool _attached;
    private bool _refreshOnAttach;

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _localization.LanguageChanged += HandleLanguageChanged;

        // Texts may have changed while the page was hidden (e.g. Settings on top).
        if (_refreshOnAttach)
        {
            _refreshOnAttach = false;
            HandleLanguageChanged(this, EventArgs.Empty);
        }
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _localization.LanguageChanged -= HandleLanguageChanged;
        _refreshOnAttach = true;
    }

    private void HandleLanguageChanged(object? sender, EventArgs e) => UpdateTexts();

    [RelayCommand]
    public async Task Create()
    {
        try
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
        catch (QrReportServerException server)
        {
            var en = _localization.IsEnglish;
            StatusMessage = server.StatusCode switch
            {
                429 => en ? "Too many links in a short time. Try again in an hour." : "მოკლე დროში ძალიან ბევრი ბმული შეიქმნა. სცადე ერთ საათში.",
                400 when server.Reason.Contains("vehicle") => en ? "The car is not identified. Connect to the car and try again." : "მანქანა ამოცნობილი არ არის. დაუკავშირდი მანქანას და სცადე თავიდან.",
                400 when server.Reason.Contains("date") => en ? "The check date is not valid. Run the check again." : "შემოწმების თარიღი არასწორია. გაიმეორე შემოწმება.",
                400 when server.Reason.Contains("large") => en ? "The report is too long for a QR link." : "ანგარიში QR ბმულისთვის ძალიან გრძელია.",
                404 or 405 => en ? "The server needs an update for QR reports." : "QR ანგარიშისთვის სერვერს განახლება სჭირდება.",
                _ => en ? $"The server could not create the link ({server.StatusCode}). Try again later." : $"სერვერმა ბმული ვერ შექმნა ({server.StatusCode}). სცადე მოგვიანებით."
            };
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.IsEnglish
                ? "Could not create the QR report. Check the internet connection and try again."
                : "QR ანგარიში ვერ შეიქმნა. შეამოწმე ინტერნეტი და სცადე თავიდან.";
            System.Diagnostics.Debug.WriteLine($"QR report creation failed: {ex}");
        }
        finally { IsBusy = false; }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "QrReportViewModel.Create");
        }
    }

    [RelayCommand]
    private async Task Share()
    {
        try
        {
        if (!HasQr) return;
        await Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = _localization.IsEnglish ? "GCar360 — QR Report" : "GCar360 — QR ანგარიში",
            Text = PublicUrl
        });
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "QrReportViewModel.Share");
        }
    }

    [RelayCommand]
    private async Task Open()
    {
        try
        {
        if (HasQr && Uri.TryCreate(PublicUrl, UriKind.Absolute, out var uri))
            await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "QrReportViewModel.Open");
        }
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
