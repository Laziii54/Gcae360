using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.PerformanceVideo;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class AccelerationPage : ContentPage
{
    public AccelerationPage(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        try
        {
        base.OnAppearing();
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
        PageTitleView.BindingContext = Shell.Current?.BindingContext;
        SemanticProperties.SetDescription(CameraButton, PerformanceVideoText.Get(PerformanceVideoText.OpenCamera));
        SafetyWarningLabel.Text = L(
            "გაზომე მხოლოდ დახურულ ტრასაზე ან ცარიელ, ლეგალურ ადგილას. დაიცავი მოძრაობის წესები.",
            "Measure only on a closed track or an empty, legal place. Obey traffic laws.");

        // One-time safety acknowledgement before the first measurement.
        if (!await EnsureSafetyAcknowledgedAsync())
            return;

        if (BindingContext is HomeViewModel viewModel)
            await viewModel.StartAccelerationPreviewAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccelerationPage.OnAppearing");
        }
    }

    private const string SafetyAckKey = "accel_safety_ack_v1";
    private bool _askingSafety;

    private static string L(string ka, string en) => CarScanner.Services.OBD.ObdLang.IsEnglish ? en : ka;

    /// <summary>
    /// Shows the safety notice until the user accepts it once. Declining
    /// leaves the page, so no measurement can start without it.
    /// </summary>
    private async Task<bool> EnsureSafetyAcknowledgedAsync()
    {
        bool acknowledged;
        try { acknowledged = Preferences.Default.Get(SafetyAckKey, false); }
        catch { acknowledged = false; }
        if (acknowledged)
            return true;
        if (_askingSafety)
            return false;

        _askingSafety = true;
        try
        {
            var accepted = await this.DisplayAlertLocalizedAsync(
                L("უსაფრთხოება", "Safety"),
                L("გაზომე მხოლოდ დახურულ ტრასაზე ან ცარიელ, ლეგალურ ადგილას. დაიცავი მოძრაობის წესები. შენს უსაფრთხოებაზე პასუხისმგებელი შენ ხარ.",
                  "Measure only on a closed track or an empty, legal place. Obey traffic laws. You are responsible for your safety."),
                L("ვეთანხმები", "Accept"),
                L("გაუქმება", "Cancel"));

            if (accepted)
            {
                try { Preferences.Default.Set(SafetyAckKey, true); }
                catch { /* asked again next time */ }
                return true;
            }

            if (Shell.Current is { } shell)
                await CarScanner.Services.SafeNavigation.GoToAsync("..");
            return false;
        }
        finally
        {
            _askingSafety = false;
        }
    }

    private async void OnCameraTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var exporter = Application.Current?.Handler?.MauiContext?.Services.GetService<IPerformanceVideoExporter>();
            if (exporter is null || !exporter.IsSupported)
            {
                await this.DisplayAlertLocalizedAsync(
                    PerformanceVideoText.Get(PerformanceVideoText.Title),
                    PerformanceVideoText.Get(PerformanceVideoText.NotSupported),
                    PerformanceVideoText.Get(PerformanceVideoText.Close));
                return;
            }

            await CarScanner.Services.SafeNavigation.GoToAsync(nameof(PerformanceVideoPage));
        }
        catch
        {
            // Never close the app because of the camera shortcut.
        }
    }

    /// <summary>Takes a picture of the result card and opens the share sheet.</summary>
    private async void OnShareResultClicked(object? sender, EventArgs e)
    {
        try
        {
            var shot = await ResultCard.CaptureAsync();
            if (shot is null)
                return;

            var label = (BindingContext as HomeViewModel)?.AccelerationHeadline ?? "0–100";
            var path = Path.Combine(FileSystem.CacheDirectory, $"obd360-{label.Replace(" ", "").Replace("–", "-").Replace("/", "")}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            await using (var source = await shot.OpenReadAsync(ScreenshotFormat.Png))
            await using (var target = File.Create(path))
                await source.CopyToAsync(target);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "OBD360 " + label,
                File = new ShareFile(path)
            });
        }
        catch
        {
            // Sharing is optional; never close the page because of it.
        }
    }

    protected override async void OnDisappearing()
    {
        try
        {
        base.OnDisappearing();
        if (BindingContext is HomeViewModel viewModel)
            await viewModel.StopAccelerationPreviewAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccelerationPage.OnDisappearing");
        }
    }
}
