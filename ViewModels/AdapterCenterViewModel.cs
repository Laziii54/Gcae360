using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CarScanner.ViewModels;

public partial class AdapterCenterViewModel : BaseViewModel
{
    private readonly OBDService _obd;
    private readonly LiveDataEngine _live;
    private readonly LocalizationService _localization;

    public ObservableCollection<AdapterTestResult> TestResults { get; } = new();

    [ObservableProperty] private string adapter = "ჯერ არ შემოწმებულა";
    [ObservableProperty] private string connection = "Bluetooth — · ELM327 — · ECU —";
    [ObservableProperty] private string benchmark = "ECU პასუხის დრო: —\nLive Data rate: —";
    [ObservableProperty] private string testState = "NOT TESTED";
    [ObservableProperty] private string testStateColor = "#94A3B8";
    [ObservableProperty] private string testStatus = "დააჭირე TEST-ს რეალური adapter/ECU შემოწმებისთვის.";
    [ObservableProperty] private int testProgress;
    [ObservableProperty] private double testProgressValue;
    [ObservableProperty] private string captureButtonText = string.Empty;
    [ObservableProperty] private string testButtonText = string.Empty;
    [ObservableProperty] private string copyLogButtonText = string.Empty;

    public AdapterCenterViewModel(OBDService obd, LiveDataEngine live, LocalizationService localization)
    {
        _obd = obd;
        _live = live;
        _localization = localization;
        Title = "OBD Adapter Center";
        RefreshCaptureText();
        RefreshIdleTexts();
        RefreshStaticButtonTexts();
        // Weak: this view model is created every time the page opens, LocalizationService lives for the whole run.
        WeakEventRelay.Attach(this, _localization,
            static (service, handler) => service.LanguageChanged += handler,
            static (service, handler) => service.LanguageChanged -= handler,
            static viewModel => MainThread.BeginInvokeOnMainThread(() =>
            {
                viewModel.RefreshCaptureText();
                viewModel.RefreshStaticButtonTexts();
                if (!viewModel.IsBusy) viewModel.RefreshIdleTexts();
            }));
    }

    /// <summary>
    /// AdapterCenterPage.xaml had "TEST" and "Copy connection log" as bare literal Button.Text (no binding at
    /// all) - confirmed on a real Android emulator to stay untranslated even though both dictionary/pack rows
    /// exist, the same root cause as the fixed Bluetooth scan-button regression. Routed through bound properties
    /// instead of the display-translation hook, like every other text on this page.
    /// </summary>
    private void RefreshStaticButtonTexts()
    {
        TestButtonText = _localization.Translate("TEST");
        CopyLogButtonText = _localization.Translate("Copy connection log");
    }

    /// <summary>The idle (not-yet-tested) state is static text with no scan data behind it, so it can be fully
    /// recomputed on a live language switch - unlike the result text a completed Test() produces, which reflects
    /// the specific adapter/ECU answers captured at that moment.</summary>
    private void RefreshIdleTexts()
    {
        if (TestState != "NOT TESTED")
            return;
        Adapter = _localization.Translate("ჯერ არ შემოწმებულა");
        TestStatus = _localization.Translate("დააჭირე TEST-ს რეალური adapter/ECU შემოწმებისთვის.");
    }

    /// <summary>
    /// Switches the raw adapter-reply capture on (for 30 minutes, then it stops by itself) or off. Off by default; nothing is
    /// recorded or sent unless it is on, and what it records is sanitized and kept in memory until it is copied here.
    /// </summary>
    [RelayCommand]
    private void ToggleCapture()
    {
        try
        {
            if (CarScanner.Services.OBD.AdapterDiagnosticCapture.IsEnabled)
                CarScanner.Services.OBD.AdapterDiagnosticCapture.Disable();
            else
                CarScanner.Services.OBD.AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(30));
            RefreshCaptureText();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AdapterCenterViewModel.ToggleCapture");
        }
    }

    /// <summary>The state of the raw capture in the chosen language, with the time that is left while it is on.</summary>
    private void RefreshCaptureText()
    {
        var remaining = CarScanner.Services.OBD.AdapterDiagnosticCapture.Remaining;
        CaptureButtonText = remaining > TimeSpan.Zero
            ? _localization.Translate($"Raw capture: ჩართულია, დარჩა დაახლოებით {(int)Math.Ceiling(remaining.TotalMinutes)} წთ — გასაჩერებლად დააჭირე")
            : _localization.Translate("Raw capture: გამორთულია — დასაწყებად დააჭირე (დიაგნოსტიკისთვის)");
    }

    /// <summary>Copies the adapter link diary (connects, losses and why, timeouts, recovery) so a dropout can be explained from facts.</summary>
    [RelayCommand]
    private async Task CopyLinkLog()
    {
        try
        {
            var linkLog = CarScanner.Services.OBD.LinkEventLog.Export();
            var capture = CarScanner.Services.OBD.AdapterDiagnosticCapture.Export();
            await Clipboard.Default.SetTextAsync(capture.Length == 0 ? linkLog : linkLog + Environment.NewLine + capture);
            TestStatus = capture.Length == 0
                ? CarScanner.Services.OBD.ObdLang.L(_localization.Translate("კავშირის ლოგი დაკოპირდა — გამომიგზავნე."), "Connection log copied — send it to me.")
                : _localization.Translate("კავშირის ლოგი და raw capture დაკოპირდა — გამომიგზავნე.");
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AdapterCenterViewModel.CopyLinkLog");
        }
    }

    [RelayCommand]
    private async Task Test()
    {
        try
        {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            TestResults.Clear();
            SetProgress(5, _localization.Translate("ტესტი იწყება…"));
            TestState = "RUNNING";
            TestStateColor = "#60A5FA";
            Adapter = _localization.Translate("ტესტი მიმდინარეობს…");
            Connection = _localization.Translate("კავშირის შემოწმება მიმდინარეობს…");
            Benchmark = _localization.Translate("რეალური პასუხის დროის მოლოდინი…");

            if (!_obd.IsInitialized)
            {
                SetProgress(12, _localization.Translate("OBD adapter-ის ინიციალიზება…"));
                await _obd.InitializeAsync();
            }

            var progress = new Progress<AdapterTestProgress>(item => SetProgress(item.Percent, item.Stage));
            var diagnostic = await _obd.GetAdapterDiagnosticsAsync(
                progress: progress,
                includeExtendedCapabilities: true);
            SetProgress(97, _localization.Translate("რეალური პასუხების შედეგების მომზადება…"));

            ApplyDiagnostics(diagnostic);
            BuildTestResults(diagnostic);
            ApplyFinalState(diagnostic);
            SetProgress(100, TestState == "PASSED" ? _localization.Translate("ტესტი წარმატებით დასრულდა.") : _localization.Translate("ტესტი დასრულდა — იხილე რეალური შედეგები ქვემოთ."));
        }
        catch (Exception ex)
        {
            TestResults.Clear();
            TestResults.Add(new AdapterTestResult
            {
                Name = "Adapter connection",
                Status = "Failed",
                Details = _localization.Translate("ტესტი ვერ დაიწყო: Bluetooth/adapter ინიციალიზაცია ვერ შესრულდა.")
            });
            Adapter = _localization.Translate("Adapter: მიუწვდომელია");
            Connection = _localization.Translate("Bluetooth/ELM327/ECU ტესტი ვერ შესრულდა.");
            Benchmark = _localization.Translate("ECU პასუხის დრო: მიუწვდომელია");
            TestState = "FAILED";
            TestStateColor = "#F87171";
            SetProgress(100, _localization.Translate($"ტესტი ვერ შესრულდა: {ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AdapterCenterViewModel.Test");
        }
    }

    private void ApplyDiagnostics(AdapterDiagnostics diagnostic)
    {
        var capability = diagnostic.CapabilitySnapshot;
        Adapter = $"Name: {diagnostic.AdapterName}\nFirmware (ATI): {diagnostic.Firmware}\nDescription: {capability?.DeviceDescription ?? "Not tested"}\nVoltage (ATRV): {diagnostic.Voltage}\nProtocol: {diagnostic.Protocol}\nProtocol number: {capability?.ProtocolNumber ?? "Not tested"}\nAddressing: {capability?.Addressing ?? "Not tested"}\nStatus: {diagnostic.OverallStatus}";
        Connection = $"Bluetooth: {Describe(diagnostic.BluetoothConnected)}\nELM327 response (ATI): {Describe(diagnostic.Elm327Responding)}\nBasic command (ATRV): {Describe(diagnostic.BasicCommandResponding)}\nVehicle ECU (0100): {Describe(diagnostic.VehicleResponding)}";

        var latency = diagnostic.EcuProbeAttempted
            ? $"{diagnostic.ProbeLatencyMs} ms"
            : "Not Supported";
        var quality = diagnostic.VehicleResponding
            ? DescribeQuality(diagnostic.ProbeLatencyMs)
            : "Not Supported";
        var liveRate = _live.DataRate > 0
            ? _localization.Translate($"{_live.DataRate} PID/s (აქტიური Live Data სესია)")
            : _localization.Translate("მიუწვდომელია — Live Data სესია არ არის აქტიური");
        var extendedBenchmark = capability is null
            ? "Capability benchmark: not tested"
            : $"Safe commands: {capability.SafeCommandsPassed}/{capability.SafeCommandsAttempted}\nLatency min/avg/max: {capability.MinimumLatencyMs}/{capability.AverageLatencyMs}/{capability.MaximumLatencyMs} ms\nCommand throughput: {capability.CommandThroughputPerSecond:0.0} request/s";
        Benchmark = _localization.Translate($"ECU პასუხის დრო (0100): {latency}\nConnection quality: {quality}\n{extendedBenchmark}\nLive Data rate: {liveRate}");
    }

    private void BuildTestResults(AdapterDiagnostics diagnostic)
    {
        AddResult(
            "Adapter connection",
            diagnostic.BluetoothConnected && diagnostic.AdapterInitialized ? "Passed" : "Failed",
            diagnostic.BluetoothConnected && diagnostic.AdapterInitialized
                ? _localization.Translate("Bluetooth კავშირი და adapter session დადასტურდა.")
                : _localization.Translate("Bluetooth კავშირი ან adapter session ვერ დადასტურდა."));

        AddResult(
            "ELM327 response (ATI)",
            diagnostic.Elm327Responding ? "Passed" : diagnostic.AdapterInitialized ? "Not Supported" : "Failed",
            diagnostic.Elm327Responding
                ? _localization.Translate($"Adapter-მა ATI ბრძანებას უპასუხა: {diagnostic.Firmware}")
                : _localization.Translate("ATI ბრძანებაზე გამოსადეგი პასუხი არ დაბრუნებულა."));

        AddResult(
            "Basic command response (ATRV)",
            diagnostic.BasicCommandResponding ? "Passed" : diagnostic.AdapterInitialized ? "Not Supported" : "Failed",
            diagnostic.BasicCommandResponding
                ? _localization.Translate($"ATRV პასუხი: {diagnostic.Voltage}")
                : _localization.Translate("Adapter-მა ATRV ბრძანებაზე გამოსადეგი პასუხი არ დააბრუნა."));

        AddResult(
            "Vehicle ECU communication (0100)",
            diagnostic.VehicleResponding ? "Passed" : diagnostic.EcuProbeAttempted ? "Not Supported" : "Failed",
            diagnostic.VehicleResponding
                ? _localization.Translate("ECU-მ სტანდარტულ OBD-II PID მხარდაჭერის მოთხოვნას უპასუხა.")
                : diagnostic.EcuProbeAttempted
                    ? _localization.Translate("ECU-მ სტანდარტული Generic OBD-II პასუხი არ დააბრუნა. ჩართე კონტაქტი ან შეამოწმე ავტომობილის OBD მხარდაჭერა.")
                    : _localization.Translate("ECU-ის 0100 მოთხოვნის გაგზავნა ვერ მოხერხდა."));

        var protocolKnown = !string.IsNullOrWhiteSpace(diagnostic.Protocol) &&
                            !diagnostic.Protocol.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
        AddResult(
            "OBD protocol",
            diagnostic.VehicleResponding && protocolKnown ? "Passed" : "Not Supported",
            protocolKnown
                ? _localization.Translate($"Adapter-მა აქტიური პროტოკოლი დააბრუნა: {diagnostic.Protocol}")
                : _localization.Translate("აქტიური OBD პროტოკოლი adapter-მა ვერ დაადასტურა."));

        AddResult(
            "ECU response time",
            diagnostic.EcuProbeAttempted ? "Passed" : "Not Supported",
            diagnostic.EcuProbeAttempted
                ? _localization.Translate($"0100 მოთხოვნის გაზომილი დრო: {diagnostic.ProbeLatencyMs} ms")
                : _localization.Translate("ECU მოთხოვნა ვერ შესრულდა, ამიტომ პასუხის დრო ვერ გაიზომა."));

        if (diagnostic.CapabilitySnapshot is not null)
        {
            foreach (var check in diagnostic.CapabilitySnapshot.Checks)
            {
                AddResult(
                    check.Name,
                    check.State switch
                    {
                        AdapterCapabilityCheckState.Passed => "Passed",
                        AdapterCapabilityCheckState.Failed => "Failed",
                        AdapterCapabilityCheckState.NotSupported => "Not Supported",
                        _ => "Not Verified"
                    },
                    check.Evidence);
            }
        }
    }

    private void ApplyFinalState(AdapterDiagnostics diagnostic)
    {
        var capabilityFailed = diagnostic.CapabilitySnapshot?.Checks.Any(
            check => check.State == AdapterCapabilityCheckState.Failed) == true;
        var requiredCanIds = new[]
        {
            "can.headers",
            "can.auto-format",
            "can.set-header",
            "can.generic-request"
        };
        var requiredCanUnavailable = diagnostic.CapabilitySnapshot?.Checks.Any(
            check => requiredCanIds.Contains(check.Id, StringComparer.OrdinalIgnoreCase) &&
                     check.State != AdapterCapabilityCheckState.Passed) == true;

        if (diagnostic.BluetoothConnected && diagnostic.AdapterInitialized && diagnostic.VehicleResponding &&
            !capabilityFailed && !requiredCanUnavailable)
        {
            TestState = "PASSED";
            TestStateColor = "#4ADE80";
            return;
        }

        if (diagnostic.BluetoothConnected && diagnostic.AdapterInitialized && diagnostic.VehicleResponding)
        {
            TestState = "PARTIAL";
            TestStateColor = "#F59E0B";
            return;
        }

        if (diagnostic.BluetoothConnected && diagnostic.AdapterInitialized)
        {
            TestState = "NOT SUPPORTED";
            TestStateColor = "#F59E0B";
            return;
        }

        TestState = "FAILED";
        TestStateColor = "#F87171";
    }

    private void AddResult(string name, string status, string details) =>
        TestResults.Add(new AdapterTestResult { Name = name, Status = status, Details = details });

    private void SetProgress(int percent, string status)
    {
        TestProgress = percent;
        TestProgressValue = percent / 100d;
        TestStatus = status;
    }

    private static string Describe(bool value) => value ? "Passed" : "Not Supported";

    private static string DescribeQuality(long latency) => latency switch
    {
        < 80 => "Excellent",
        < 250 => "Good",
        _ => "Slow"
    };
}
