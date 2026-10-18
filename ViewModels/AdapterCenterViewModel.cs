using CarScanner.Models;
using CarScanner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CarScanner.ViewModels;

public partial class AdapterCenterViewModel : BaseViewModel
{
    private readonly OBDService _obd;
    private readonly LiveDataEngine _live;

    public ObservableCollection<AdapterTestResult> TestResults { get; } = new();

    [ObservableProperty] private string adapter = "ჯერ არ შემოწმებულა";
    [ObservableProperty] private string connection = "Bluetooth — · ELM327 — · ECU —";
    [ObservableProperty] private string benchmark = "ECU პასუხის დრო: —\nLive Data rate: —";
    [ObservableProperty] private string testState = "NOT TESTED";
    [ObservableProperty] private string testStateColor = "#94A3B8";
    [ObservableProperty] private string testStatus = "დააჭირე TEST-ს რეალური adapter/ECU შემოწმებისთვის.";
    [ObservableProperty] private int testProgress;
    [ObservableProperty] private double testProgressValue;

    public AdapterCenterViewModel(OBDService obd, LiveDataEngine live)
    {
        _obd = obd;
        _live = live;
        Title = "OBD Adapter Center";
    }

    /// <summary>Copies the adapter link diary (connects, losses and why, timeouts, recovery) so a dropout can be explained from facts.</summary>
    [RelayCommand]
    private async Task CopyLinkLog()
    {
        try
        {
            await Clipboard.Default.SetTextAsync(CarScanner.Services.OBD.LinkEventLog.Export());
            TestStatus = CarScanner.Services.OBD.ObdLang.L("კავშირის ლოგი დაკოპირდა — გამომიგზავნე.", "Connection log copied — send it to me.");
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
            SetProgress(5, "ტესტი იწყება…");
            TestState = "RUNNING";
            TestStateColor = "#60A5FA";
            Adapter = "ტესტი მიმდინარეობს…";
            Connection = "კავშირის შემოწმება მიმდინარეობს…";
            Benchmark = "რეალური პასუხის დროის მოლოდინი…";

            if (!_obd.IsInitialized)
            {
                SetProgress(12, "OBD adapter-ის ინიციალიზება…");
                await _obd.InitializeAsync();
            }

            var progress = new Progress<AdapterTestProgress>(item => SetProgress(item.Percent, item.Stage));
            var diagnostic = await _obd.GetAdapterDiagnosticsAsync(
                progress: progress,
                includeExtendedCapabilities: true);
            SetProgress(97, "რეალური პასუხების შედეგების მომზადება…");

            ApplyDiagnostics(diagnostic);
            BuildTestResults(diagnostic);
            ApplyFinalState(diagnostic);
            SetProgress(100, TestState == "PASSED" ? "ტესტი წარმატებით დასრულდა." : "ტესტი დასრულდა — იხილე რეალური შედეგები ქვემოთ.");
        }
        catch (Exception ex)
        {
            TestResults.Clear();
            TestResults.Add(new AdapterTestResult
            {
                Name = "Adapter connection",
                Status = "Failed",
                Details = "ტესტი ვერ დაიწყო: Bluetooth/adapter ინიციალიზაცია ვერ შესრულდა."
            });
            Adapter = "Adapter: მიუწვდომელია";
            Connection = "Bluetooth/ELM327/ECU ტესტი ვერ შესრულდა.";
            Benchmark = "ECU პასუხის დრო: მიუწვდომელია";
            TestState = "FAILED";
            TestStateColor = "#F87171";
            SetProgress(100, $"ტესტი ვერ შესრულდა: {ex.Message}");
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
            ? $"{_live.DataRate} PID/s (აქტიური Live Data სესია)"
            : "მიუწვდომელია — Live Data სესია არ არის აქტიური";
        var extendedBenchmark = capability is null
            ? "Capability benchmark: not tested"
            : $"Safe commands: {capability.SafeCommandsPassed}/{capability.SafeCommandsAttempted}\nLatency min/avg/max: {capability.MinimumLatencyMs}/{capability.AverageLatencyMs}/{capability.MaximumLatencyMs} ms\nCommand throughput: {capability.CommandThroughputPerSecond:0.0} request/s";
        Benchmark = $"ECU პასუხის დრო (0100): {latency}\nConnection quality: {quality}\n{extendedBenchmark}\nLive Data rate: {liveRate}";
    }

    private void BuildTestResults(AdapterDiagnostics diagnostic)
    {
        AddResult(
            "Adapter connection",
            diagnostic.BluetoothConnected && diagnostic.AdapterInitialized ? "Passed" : "Failed",
            diagnostic.BluetoothConnected && diagnostic.AdapterInitialized
                ? "Bluetooth კავშირი და adapter session დადასტურდა."
                : "Bluetooth კავშირი ან adapter session ვერ დადასტურდა.");

        AddResult(
            "ELM327 response (ATI)",
            diagnostic.Elm327Responding ? "Passed" : diagnostic.AdapterInitialized ? "Not Supported" : "Failed",
            diagnostic.Elm327Responding
                ? $"Adapter-მა ATI ბრძანებას უპასუხა: {diagnostic.Firmware}"
                : "ATI ბრძანებაზე გამოსადეგი პასუხი არ დაბრუნებულა.");

        AddResult(
            "Basic command response (ATRV)",
            diagnostic.BasicCommandResponding ? "Passed" : diagnostic.AdapterInitialized ? "Not Supported" : "Failed",
            diagnostic.BasicCommandResponding
                ? $"ATRV პასუხი: {diagnostic.Voltage}"
                : "Adapter-მა ATRV ბრძანებაზე გამოსადეგი პასუხი არ დააბრუნა.");

        AddResult(
            "Vehicle ECU communication (0100)",
            diagnostic.VehicleResponding ? "Passed" : diagnostic.EcuProbeAttempted ? "Not Supported" : "Failed",
            diagnostic.VehicleResponding
                ? "ECU-მ სტანდარტულ OBD-II PID მხარდაჭერის მოთხოვნას უპასუხა."
                : diagnostic.EcuProbeAttempted
                    ? "ECU-მ სტანდარტული Generic OBD-II პასუხი არ დააბრუნა. ჩართე კონტაქტი ან შეამოწმე ავტომობილის OBD მხარდაჭერა."
                    : "ECU-ის 0100 მოთხოვნის გაგზავნა ვერ მოხერხდა.");

        var protocolKnown = !string.IsNullOrWhiteSpace(diagnostic.Protocol) &&
                            !diagnostic.Protocol.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
        AddResult(
            "OBD protocol",
            diagnostic.VehicleResponding && protocolKnown ? "Passed" : "Not Supported",
            protocolKnown
                ? $"Adapter-მა აქტიური პროტოკოლი დააბრუნა: {diagnostic.Protocol}"
                : "აქტიური OBD პროტოკოლი adapter-მა ვერ დაადასტურა.");

        AddResult(
            "ECU response time",
            diagnostic.EcuProbeAttempted ? "Passed" : "Not Supported",
            diagnostic.EcuProbeAttempted
                ? $"0100 მოთხოვნის გაზომილი დრო: {diagnostic.ProbeLatencyMs} ms"
                : "ECU მოთხოვნა ვერ შესრულდა, ამიტომ პასუხის დრო ვერ გაიზომა.");

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
            "can.flow-control",
            "can.set-header",
            "can.receive-filter-command",
            "can.receive-filter-verified",
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
