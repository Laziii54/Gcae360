using CarScanner.VinCheck.Services;

namespace CarScanner.VinCheck.Views;

/// <summary>
/// Live VIN scanner: the camera looks at the VIN (windshield corner, door
/// frame or registration paper), the text is read on the phone and the VIN
/// is filled in automatically as soon as it is recognised.
/// </summary>
public partial class VinScanPage : ContentPage
{
    private readonly VinTextExtractor _extractor = new();
    private CancellationTokenSource? _scan;
    private bool _visible;
    private bool _torch;
    private bool _done;

    public VinScanPage()
    {
        InitializeComponent();
        CameraView.ControllerChanged += (_, _) => { if (_visible) _ = StartAsync(); };
        TitleLabel.Text = VinText.T("ScanTitle");
        HintLabel.Text = VinText.T("ScanHint");
        StatusLabel.Text = VinText.T("ScanLooking");
        SettingsButton.Text = VinText.T("ScanOpenSettings");
    }

    protected override async void OnAppearing()
    {
        try
        {
        base.OnAppearing();
        _visible = true;
        try { DeviceDisplay.Current.KeepScreenOn = true; } catch { }

        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.Camera>();

        if (status != PermissionStatus.Granted)
        {
            ShowMessage(VinText.T("ScanNoPermission"), spinning: false);
            SettingsButton.IsVisible = true;
            return;
        }

        await Task.Delay(150);
        await StartAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinScanPage.OnAppearing");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _visible = false;
        _scan?.Cancel();
        try { DeviceDisplay.Current.KeepScreenOn = false; } catch { }
        try { CameraView.Controller?.Release(); } catch { }
    }

    private async Task StartAsync()
    {
        if (_scan is not null || CameraView.Controller is not { } controller)
            return;

        _scan = new CancellationTokenSource();
        var token = _scan.Token;

        try
        {
            await controller.StartPreviewAsync();
        }
        catch
        {
            ShowMessage(VinText.T("ScanCameraError"), spinning: false);
            return;
        }

        if (controller is not IVinCameraReader reader)
        {
            ShowMessage(VinText.T("ScanUnsupported"), spinning: false);
            return;
        }

        TorchButton.IsVisible = reader.HasTorch;
        _extractor.Reset();

        // A short pause so the camera can focus, then a picture every ~0.7 s.
        await Task.Delay(600);
        while (!token.IsCancellationRequested && !_done)
        {
            try
            {
                var lines = await reader.ReadTextLinesAsync(token);
                var vin = _extractor.Accept(lines);
                if (vin is not null)
                {
                    await FoundAsync(vin);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // One unreadable picture: try the next one.
            }

            try { await Task.Delay(700, token); } catch { return; }
        }
    }

    private async Task FoundAsync(string vin)
    {
        _done = true;
        ScanFrame.Stroke = Color.FromArgb("#22C55E");
        StatusPill.BackgroundColor = Color.FromArgb("#E616A34A");
        ShowMessage("" + vin, spinning: false);
        try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); } catch { }

        VinScanBridge.Publish(vin);
        await Task.Delay(700);
        try { await CarScanner.Services.SafeNavigation.GoToAsync(".."); } catch { }
    }

    private void ShowMessage(string text, bool spinning)
    {
        StatusLabel.Text = text;
        Spinner.IsRunning = spinning;
        Spinner.IsVisible = spinning;
    }

    private async void OnTorchClicked(object? sender, EventArgs e)
    {
        try
        {
        if (CameraView.Controller is not IVinCameraReader reader)
            return;
        _torch = !_torch;
        TorchButton.BackgroundColor = _torch ? Color.FromArgb("#F59E0B") : Color.FromArgb("#66000000");
        try { await reader.SetTorchAsync(_torch); } catch { }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinScanPage.OnTorchClicked");
        }
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        try { await CarScanner.Services.SafeNavigation.GoToAsync(".."); } catch { }
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        try { AppInfo.Current.ShowSettingsUI(); } catch { }
    }
}
