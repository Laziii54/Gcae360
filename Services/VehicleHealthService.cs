using System.Globalization;
using CarScanner.Models;
using CarScanner.Services.Localization;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Services;

/// <summary>
/// Aggregates the existing generic and verified OEM diagnostic readers into a
/// deterministic health result. Missing/unsupported data affects coverage only.
/// </summary>
public sealed class VehicleHealthService
{
    private readonly IBluetoothService _bluetooth;
    private readonly FullObdScanService _fullScan;
    private readonly OemModuleDiscoveryService _moduleDiscovery;
    private readonly OemEcuDtcService _oemDtc;
    private readonly LocalizationService _localization;

    public VehicleHealthService(
        IBluetoothService bluetooth,
        FullObdScanService fullScan,
        OemModuleDiscoveryService moduleDiscovery,
        OemEcuDtcService oemDtc,
        LocalizationService localization)
    {
        _bluetooth = bluetooth;
        _fullScan = fullScan;
        _moduleDiscovery = moduleDiscovery;
        _oemDtc = oemDtc;
        _localization = localization;
    }

    public async Task<VehicleHealthReport> ScanAsync(
        IProgress<VehicleHealthProgress>? progress = null,
        CancellationToken token = default)
    {
        if (!_bluetooth.IsConnected)
            throw new InvalidOperationException(L("The OBD2 adapter is not connected.", "OBD2 ადაპტერი არ არის დაკავშირებული."));

        using var disconnectCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, disconnectCts.Token);
        var scanToken = linkedCts.Token;
        void OnConnectionChanged(object? _, bool connected)
        {
            if (!connected) disconnectCts.Cancel();
        }

        _bluetooth.ConnectionChanged += OnConnectionChanged;
        try
        {
            progress?.Report(new VehicleHealthProgress(0, L("Starting full health scan…", "სრული მდგომარეობის სკანირება იწყება…")));
            var genericProgress = new Progress<ModuleScanProgress>(stage =>
            {
                var mapped = Math.Clamp((int)Math.Round(stage.Percent * 0.70d), 1, 70);
                progress?.Report(new VehicleHealthProgress(mapped, GenericStageText(stage.Percent, stage.Stage)));
            });

            var report = await _fullScan.ScanAsync(
                includeExpandedSensors: true,
                progress: genericProgress,
                token: scanToken).ConfigureAwait(false);

            scanToken.ThrowIfCancellationRequested();
            if (!report.EcuResponded)
                throw new InvalidOperationException(L(
                    "The adapter is connected, but no usable vehicle ECU response was received.",
                    "ადაპტერი დაკავშირებულია, მაგრამ მანქანის ECU-ს გამოსაყენებელი პასუხი არ მიუღია."));

            progress?.Report(new VehicleHealthProgress(74, L("Checking accessible vehicle modules…", "ხელმისაწვდომი ECU მოდულები მოწმდება…")));
            OemModuleDiscoveryResult? discovery = null;
            OemDtcScanResult? oemCodes = null;
            try
            {
                discovery = await _moduleDiscovery.DiscoverAsync(report.Vehicle, scanToken).ConfigureAwait(false);
                scanToken.ThrowIfCancellationRequested();
                if (discovery.Probes.Any(probe => probe.State is OemModuleProbeState.Available or OemModuleProbeState.RespondedLimited))
                {
                    progress?.Report(new VehicleHealthProgress(82, L("Reading accessible module trouble codes…", "ხელმისაწვდომი მოდულების კოდები იკითხება…")));
                    oemCodes = await _oemDtc.ReadAsync(report.Vehicle, discovery, token: scanToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (scanToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // OEM access is optional. Adapter/profile limitations never
                // turn into a health problem or stop the generic result.
            }

            progress?.Report(new VehicleHealthProgress(90, L("Evaluating diagnostic evidence…", "მიღებული დიაგნოსტიკური მონაცემები ფასდება…")));
            var result = BuildResult(report, discovery, oemCodes);
            scanToken.ThrowIfCancellationRequested();
            progress?.Report(new VehicleHealthProgress(100, L("Health analysis complete", "მდგომარეობის შეფასება დასრულდა")));
            return result;
        }
        finally
        {
            _bluetooth.ConnectionChanged -= OnConnectionChanged;
        }
    }

    private VehicleHealthReport BuildResult(
        ObdScanReport report,
        OemModuleDiscoveryResult? discovery,
        OemDtcScanResult? oemCodes)
    {
        var issues = new List<VehicleHealthIssue>();
        var issueGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dtcs = CollectUniqueDtcs(report, oemCodes);

        foreach (var dtc in dtcs)
        {
            var classification = ClassifyDtc(dtc.Code, dtc.Description);
            var penalty = dtc.PendingOnly
                ? Math.Max(1, (int)Math.Ceiling(classification.Penalty / 2d))
                : dtc.PermanentOnly
                    ? Math.Max(1, (int)Math.Ceiling(classification.Penalty / 2d))
                    : classification.Penalty;
            issueGroups.Add(classification.Group);
            issues.Add(new VehicleHealthIssue
            {
                Code = dtc.Code,
                Title = string.IsNullOrWhiteSpace(dtc.Description) ? dtc.Code : $"{dtc.Code} · {dtc.Description}",
                Description = dtc.PendingOnly
                    ? L("Pending trouble code", "მომლოდინე შეცდომის კოდი")
                    : dtc.PermanentOnly
                        ? L("Permanent/history trouble code", "მუდმივი/ისტორიული შეცდომის კოდი")
                        : L("Stored or confirmed trouble code", "შენახული ან დადასტურებული შეცდომის კოდი"),
                Severity = classification.Severity,
                Category = classification.Category,
                Penalty = penalty,
                Source = dtc.Source,
                SeverityText = SeverityText(classification.Severity)
            });
        }

        if (report.Readiness?.MilOn == true && dtcs.Count == 0)
        {
            issues.Add(new VehicleHealthIssue
            {
                Code = "MIL",
                Title = L("Malfunction indicator lamp is ON", "გაუმართაობის ინდიკატორი ჩართულია"),
                Description = L(
                    "The ECU reports the warning lamp as active, but no readable DTC was returned in this scan.",
                    "ECU აფიქსირებს ჩართულ გაფრთხილების ნათურას, თუმცა ამ სკანზე წაკითხვადი DTC არ დაბრუნდა."),
                Severity = VehicleHealthSeverity.Major,
                Category = VehicleHealthCategory.Engine,
                Penalty = 8,
                Source = "Mode 01 PID 01",
                SeverityText = SeverityText(VehicleHealthSeverity.Major)
            });
        }

        var sensorIssues = AnalyzeSensors(report.Sensors, report.Vehicle, issueGroups);
        issues.AddRange(sensorIssues);

        var incompleteReadiness = report.ReadinessRead && report.Readiness is not null
            ? CountIncompleteReadinessMonitors(report.Readiness.RawBytes)
            : 0;
        if (incompleteReadiness > 0)
        {
            issues.Add(new VehicleHealthIssue
            {
                Code = "READINESS",
                Title = L("Incomplete emissions monitors", "დაუსრულებელი გამონაბოლქვის მონიტორები"),
                Description = L(
                    $"{incompleteReadiness} supported readiness monitor(s) are incomplete.",
                    $"{incompleteReadiness} მხარდაჭერილი readiness მონიტორი დაუსრულებელია."),
                Severity = VehicleHealthSeverity.Minor,
                Category = VehicleHealthCategory.Emissions,
                Penalty = Math.Min(8, incompleteReadiness),
                Source = "Mode 01 PID 01",
                SeverityText = SeverityText(VehicleHealthSeverity.Minor)
            });
        }

        var score = VehicleHealthScoreCalculator.Calculate(issues, incompleteReadiness);

        var respondingModules = discovery?.Probes.Count(probe =>
            probe.State is OemModuleProbeState.Available or OemModuleProbeState.RespondedLimited) ?? 0;
        var availableChecks = 6 + report.PidStats.Advertised + (discovery?.Probes.Count ?? 0);
        var checkedChecks = 1 + report.DtcServicesRead + (report.VehicleInfoRead ? 1 : 0) +
                            (report.ReadinessRead ? 1 : 0) + report.PidStats.Decoded + respondingModules;
        var coverage = availableChecks <= 0
            ? 0
            : Math.Clamp((int)Math.Round(checkedChecks * 100d / availableChecks), 0, 100);
        var orderedIssues = issues
            .OrderByDescending(issue => issue.Severity)
            .ThenByDescending(issue => issue.Penalty)
            .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new VehicleHealthReport
        {
            Score = score,
            CriticalIssueCount = orderedIssues.Count(issue => issue.Severity == VehicleHealthSeverity.Critical),
            WarningCount = orderedIssues.Count(issue => issue.Penalty > 0 && issue.Severity != VehicleHealthSeverity.Critical),
            Issues = orderedIssues,
            CheckedSensorCount = report.PidStats.Decoded,
            CheckedModuleCount = 1 + respondingModules,
            ScanCoverage = coverage,
            CheckedChecks = checkedChecks,
            AvailableChecks = availableChecks,
            Interpretation = Interpretation(score),
            Summary = L(
                $"Checked {report.PidStats.Decoded} sensors and {1 + respondingModules} responding ECU module(s). Health reflects detected problems only; unavailable checks did not lower the score.",
                $"შემოწმდა {report.PidStats.Decoded} სენსორი და {1 + respondingModules} პასუხმგებელი ECU მოდული. ქულა მხოლოდ აღმოჩენილ პრობლემებს ასახავს; მიუწვდომელ შემოწმებებს ქულა არ დაუკლია.")
        };
    }

    private IReadOnlyList<VehicleHealthIssue> AnalyzeSensors(
        IReadOnlyList<SensorValue> sensors,
        VehicleInfo vehicle,
        ISet<string> dtcGroups)
    {
        var issues = new List<VehicleHealthIssue>();
        var byPid = sensors.GroupBy(sensor => sensor.Pid).ToDictionary(group => group.Key, group => group.Last());
        var rpm = Value(byPid, 0x0C);
        var coolant = Value(byPid, 0x05);
        var engineRunning = rpm is > 400;

        if (coolant is > 120)
        {
            var penalty = dtcGroups.Contains("overheat") ? 3 : 15;
            issues.Add(SensorIssue("PID 0105", L("Critical coolant temperature", "ძრავის კრიტიკული ტემპერატურა"),
                L($"Coolant temperature is {coolant:0.#} °C.", $"გამაგრილებლის ტემპერატურაა {coolant:0.#} °C."),
                VehicleHealthSeverity.Critical, VehicleHealthCategory.Temperature, penalty, "overheat"));
        }
        else if (coolant is >= 110)
        {
            var penalty = dtcGroups.Contains("overheat") ? 2 : 5;
            issues.Add(SensorIssue("PID 0105", L("High coolant temperature", "ძრავის მაღალი ტემპერატურა"),
                L($"Coolant temperature is {coolant:0.#} °C.", $"გამაგრილებლის ტემპერატურაა {coolant:0.#} °C."),
                VehicleHealthSeverity.Warning, VehicleHealthCategory.Temperature, penalty, "overheat"));
        }

        var fuel = (vehicle.FuelType ?? string.Empty).ToLowerInvariant();
        var conventionalCharging = !fuel.Contains("electric") && !fuel.Contains("ელექტრო") &&
                                   !fuel.Contains("hybrid") && !fuel.Contains("ჰიბრიდ");
        var voltage = Value(byPid, 0x42);
        if (engineRunning && conventionalCharging && voltage is not null)
        {
            if (voltage < 11.8 || voltage > 15.5)
                issues.Add(SensorIssue("PID 0142", L("Charging voltage outside safe range", "დამუხტვის ძაბვა უსაფრთხო დიაპაზონს გარეთაა"),
                    L($"Control-module voltage is {voltage:0.##} V while the engine is running.", $"მუშა ძრავზე control-module voltage არის {voltage:0.##} V."),
                    VehicleHealthSeverity.Major, VehicleHealthCategory.Electrical, 8, "voltage"));
            else if (voltage < 13.0)
                issues.Add(SensorIssue("PID 0142", L("Low charging voltage", "დამუხტვის დაბალი ძაბვა"),
                    L($"Control-module voltage is {voltage:0.##} V while the engine is running.", $"მუშა ძრავზე control-module voltage არის {voltage:0.##} V."),
                    VehicleHealthSeverity.Warning, VehicleHealthCategory.Electrical, 3, "voltage"));
        }

        if (engineRunning && (coolant is null || coolant >= 60))
        {
            EvaluateFuelTrimBank(1, Value(byPid, 0x06), Value(byPid, 0x07), issues, dtcGroups);
            EvaluateFuelTrimBank(2, Value(byPid, 0x08), Value(byPid, 0x09), issues, dtcGroups);
        }

        return issues;
    }

    private void EvaluateFuelTrimBank(
        int bank,
        double? shortTrim,
        double? longTrim,
        ICollection<VehicleHealthIssue> issues,
        ISet<string> dtcGroups)
    {
        if (shortTrim is null || longTrim is null) return;
        var combined = Math.Abs(shortTrim.Value + longTrim.Value);
        if (combined <= 10) return;

        var severity = combined > 30 ? VehicleHealthSeverity.Major
            : combined > 20 ? VehicleHealthSeverity.Warning
            : VehicleHealthSeverity.Minor;
        var basePenalty = combined > 30 ? 8 : combined > 20 ? 5 : 2;
        var penalty = dtcGroups.Contains("fuel-trim") ? Math.Min(2, basePenalty) : basePenalty;
        issues.Add(SensorIssue(
            $"FUEL-TRIM-B{bank}",
            L($"Fuel trim deviation — bank {bank}", $"საწვავის კორექციის გადახრა — bank {bank}"),
            L(
                $"STFT {shortTrim:0.#}% + LTFT {longTrim:0.#}% (absolute combined {combined:0.#}%).",
                $"STFT {shortTrim:0.#}% + LTFT {longTrim:0.#}% (ჯამური მოდული {combined:0.#}%)."),
            severity,
            VehicleHealthCategory.FuelSystem,
            penalty,
            "fuel-trim"));
    }

    private VehicleHealthIssue SensorIssue(
        string code,
        string title,
        string description,
        VehicleHealthSeverity severity,
        VehicleHealthCategory category,
        int penalty,
        string source) => new()
    {
        Code = code,
        Title = title,
        Description = description,
        Severity = severity,
        Category = category,
        Penalty = penalty,
        Source = $"OBD live data · {source}",
        SeverityText = SeverityText(severity)
    };

    private IReadOnlyList<HealthDtcEvidence> CollectUniqueDtcs(
        ObdScanReport report,
        OemDtcScanResult? oemCodes)
    {
        var all = new List<HealthDtcEvidence>();
        all.AddRange(report.StoredCodes.Select(code => new HealthDtcEvidence(code.Code, code.Description, false, false, L("Generic OBD · Stored", "Generic OBD · შენახული"))));
        all.AddRange(report.PendingCodes.Select(code => new HealthDtcEvidence(code.Code, code.Description, true, false, L("Generic OBD · Pending", "Generic OBD · მომლოდინე"))));
        all.AddRange(report.PermanentCodes.Select(code => new HealthDtcEvidence(code.Code, code.Description, false, true, L("Generic OBD · Permanent", "Generic OBD · მუდმივი"))));
        if (oemCodes is not null)
        {
            foreach (var ecu in oemCodes.Ecus.Where(ecu => ecu.State == OemDtcReadState.Available))
            foreach (var code in ecu.Records)
            {
                var status = (UdsDtcStatus)code.StatusByte;
                all.Add(new HealthDtcEvidence(
                    code.DisplayCode,
                    code.Description,
                    status.HasFlag(UdsDtcStatus.Pending) && !status.HasFlag(UdsDtcStatus.Confirmed),
                    false,
                    $"{ecu.EcuName} · OEM DTC"));
            }
        }

        return all
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .GroupBy(item => NormalizeCode(item.Code), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => item.PendingOnly ? 2 : item.PermanentOnly ? 1 : 0)
                .First())
            .ToArray();
    }

    private static DtcClassification ClassifyDtc(string rawCode, string description)
    {
        var code = NormalizeCode(rawCode);
        var text = (description ?? string.Empty).ToLowerInvariant();
        if (code == "P0217" || code == "P0524" || code == "P0AA6" ||
            text.Contains("oil pressure low") || text.Contains("overheat") || text.Contains("high voltage isolation"))
            return new(VehicleHealthSeverity.Critical, VehicleHealthCategory.Engine, 15,
                code == "P0217" ? "overheat" : code == "P0524" ? "oil-pressure" : "high-voltage");

        if (code.StartsWith('C') && (text.Contains("brake") || text.Contains("abs")))
            return new(VehicleHealthSeverity.Critical, VehicleHealthCategory.Abs, 15, "brakes");
        if (code.StartsWith('B') && (text.Contains("airbag") || text.Contains("srs") || text.Contains("restraint")))
            return new(VehicleHealthSeverity.Critical, VehicleHealthCategory.Srs, 15, "restraint");
        if (code.StartsWith("P03", StringComparison.Ordinal))
            return new(VehicleHealthSeverity.Major, VehicleHealthCategory.Engine, 8, "misfire");
        if (code.StartsWith("P07", StringComparison.Ordinal))
            return new(VehicleHealthSeverity.Major, VehicleHealthCategory.Transmission, 8, "transmission");
        if (code.StartsWith('U'))
            return new(VehicleHealthSeverity.Major, VehicleHealthCategory.Communication, 8, "communication");
        if (code.StartsWith("P017", StringComparison.Ordinal) || code.StartsWith("P019", StringComparison.Ordinal))
            return new(VehicleHealthSeverity.Major, VehicleHealthCategory.FuelSystem, 8, "fuel-trim");
        if (code is "P0562" or "P0563")
            return new(VehicleHealthSeverity.Major, VehicleHealthCategory.Electrical, 8, "voltage");
        if (code.StartsWith("P04", StringComparison.Ordinal) || code.StartsWith("P042", StringComparison.Ordinal) || code.StartsWith("P043", StringComparison.Ordinal))
            return new(VehicleHealthSeverity.Minor, VehicleHealthCategory.Emissions, 3, "emissions");
        if (code.StartsWith('B'))
            return new(VehicleHealthSeverity.Warning, VehicleHealthCategory.Other, 3, "body");
        if (code.StartsWith('C'))
            return new(VehicleHealthSeverity.Major, VehicleHealthCategory.Other, 8, "chassis");
        return new(VehicleHealthSeverity.Minor, VehicleHealthCategory.Dtc, 3, code);
    }

    private static int CountIncompleteReadinessMonitors(byte[] raw)
    {
        if (raw.Length < 4) return 0;
        var supportedContinuous = raw[1] & 0x07;
        var incompleteContinuous = (raw[1] >> 4) & 0x07;
        var supportedNonContinuous = raw[2];
        var incompleteNonContinuous = raw[3];
        return CountBits(supportedContinuous & incompleteContinuous) +
               CountBits(supportedNonContinuous & incompleteNonContinuous);
    }

    private static int CountBits(int value)
    {
        var count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }

    private static double? Value(IReadOnlyDictionary<int, SensorValue> values, int pid)
    {
        if (!values.TryGetValue(pid, out var sensor)) return null;
        var raw = string.IsNullOrWhiteSpace(sensor.CanonicalValue) ? sensor.Value : sensor.CanonicalValue;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private string Interpretation(int score) => score switch
    {
        >= 90 => L("Excellent / Good condition", "შესანიშნავი / კარგი მდგომარეობა"),
        >= 75 => L("Generally good — minor issues detected", "ზოგადად კარგი — აღმოჩენილია მცირე საკითხები"),
        >= 60 => L("Attention recommended", "რეკომენდებულია ყურადღება"),
        >= 40 => L("Multiple or significant issues detected", "აღმოჩენილია რამდენიმე ან მნიშვნელოვანი პრობლემა"),
        _ => L("Serious issues detected", "აღმოჩენილია სერიოზული პრობლემები")
    };

    private string SeverityText(VehicleHealthSeverity severity) => severity switch
    {
        VehicleHealthSeverity.Critical => L("Critical", "კრიტიკული"),
        VehicleHealthSeverity.Major => L("Major", "მნიშვნელოვანი"),
        VehicleHealthSeverity.Warning => L("Warning", "გაფრთხილება"),
        VehicleHealthSeverity.Minor => L("Minor", "მცირე"),
        _ => L("Information", "ინფორმაცია")
    };

    private string GenericStageText(int percent, string original) => percent switch
    {
        <= 10 => L("Checking vehicle connection…", "ავტომობილთან კავშირი მოწმდება…"),
        <= 20 => L("Reading vehicle identification…", "ავტომობილის იდენტიფიკაცია იკითხება…"),
        <= 55 => L("Reading diagnostic trouble codes…", "შეცდომის კოდები იკითხება…"),
        <= 70 => L("Checking emissions and readiness…", "ემისია და readiness მოწმდება…"),
        < 88 => L("Reading supported sensor values…", original),
        _ => L("Finalizing standard OBD checks…", "სტანდარტული OBD შემოწმებები ჯამდება…")
    };

    private string L(string english, string georgian) => _localization.IsEnglish ? english : georgian;

    private static string NormalizeCode(string code) =>
        new string((code ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private sealed record HealthDtcEvidence(
        string Code,
        string Description,
        bool PendingOnly,
        bool PermanentOnly,
        string Source);

    private sealed record DtcClassification(
        VehicleHealthSeverity Severity,
        VehicleHealthCategory Category,
        int Penalty,
        string Group);
}
