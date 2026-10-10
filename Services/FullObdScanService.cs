using CarScanner.Models;
using CarScanner.Database;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using System.Text.Json;

namespace CarScanner.Services;

/// <summary>
/// Orchestrates a complete generic OBD-II emissions scan. It intentionally does not
/// pretend to discover proprietary ABS/SRS/BCM ECUs: those require OEM-specific
/// addressing and databases and are exposed as a separate extension point.
/// </summary>
public sealed class FullObdScanService
{
    private static readonly int[] ModuleSnapshotPids = { 0x0C, 0x0D, 0x04, 0x05, 0x11, 0x42 };
    private readonly OBDService _obd;
    private readonly VehicleInfoService _vehicle;
    private readonly AppDatabase _db;
    private readonly ModuleDtcScanService _modules;

    public FullObdScanService(OBDService obd, VehicleInfoService vehicle, AppDatabase db, ModuleDtcScanService modules)
    { _obd = obd; _vehicle = vehicle; _db = db; _modules = modules; }

    public async Task<ObdScanReport> ScanAsync(
        IProgress<ModuleScanProgress>? progress = null,
        CancellationToken token = default)
        => await ScanAsync(false, progress, token).ConfigureAwait(false);

    public async Task<ObdScanReport> ScanAsync(
        bool includeExpandedSensors,
        IProgress<ModuleScanProgress>? progress = null,
        CancellationToken token = default)
    {
        var started = DateTime.Now;
        progress?.Report(new ModuleScanProgress(2, "OBD adapter და კავშირის შემოწმება…"));
        if (!_obd.IsInitialized)
            await _obd.InitializeAsync(token);

        var notes = new List<string>();

        // Probe the actual generic OBD endpoint first. If it does not answer,
        // do not make the user wait through VIN, DTC and PID timeouts.
        progress?.Report(new ModuleScanProgress(10, "სტანდარტული OBD ECU პასუხის დადასტურება…"));
        var adapter = await _obd.GetAdapterDiagnosticsAsync(token);
        if (!adapter.VehicleResponding)
        {
            progress?.Report(new ModuleScanProgress(100, "სტანდარტული OBD ECU პასუხი ვერ დადასტურდა."));
            var unavailableReport = new ObdScanReport
            {
                StartedAt = started,
                FinishedAt = DateTime.Now,
                Protocol = _obd.CurrentProtocolDescription,
                AdapterName = adapter.AdapterName,
                AdapterVoltage = adapter.Voltage,
                EcuResponded = false,
                Notes = "Generic OBD-II ECU პასუხი ვერ დადასტურდა. სხვა მოდულები არ არის გამოგონილი და არ დასკანირებულა."
            };
            await PersistAsync(unavailableReport);
            return unavailableReport;
        }

        progress?.Report(new ModuleScanProgress(18, "Vehicle / VIN ინფორმაციის წაკითხვა…"));
        var vehicle = new VehicleInfo();
        var vehicleInfoRead = true;
        try { vehicle = await _vehicle.IdentifyConnectedVehicleAsync(token); }
        catch (OperationCanceledException) { throw; }
        catch { vehicleInfoRead = false; notes.Add("Vehicle / VIN ინფორმაცია მიუწვდომელია."); }

        progress?.Report(new ModuleScanProgress(30, "Stored DTC კოდების წაკითხვა…"));
        var storedResult = await ReadDtcSafelyAsync(() => _obd.ReadStoredDtcAsync(token), "Stored DTC", notes);
        storedResult = RequireReply(storedResult, _obd.LastDtcReadGotNoReply("03"), "Stored DTC", notes);

        progress?.Report(new ModuleScanProgress(40, "Pending DTC კოდების წაკითხვა…"));
        var pendingResult = await ReadDtcSafelyAsync(() => _obd.ReadPendingDtcAsync(token), "Pending DTC", notes);
        pendingResult = RequireReply(pendingResult, _obd.LastDtcReadGotNoReply("07"), "Pending DTC", notes);

        progress?.Report(new ModuleScanProgress(50, "Permanent DTC კოდების წაკითხვა…"));
        var permanentResult = await ReadDtcSafelyAsync(() => _obd.ReadPermanentDtcAsync(token), "Permanent DTC", notes);
        // Permanent codes (service 0A) do not exist on older vehicles; "no reply" there is not evidence of a problem.
        if (permanentResult.IsAvailable && _obd.LastDtcReadGotNoReply("0A"))
            notes.Add("Permanent DTC: the vehicle did not reply to this service (not supported by many vehicles), so it is not reported as 'no codes'.");
        var stored = storedResult.Codes;
        var pending = pendingResult.Codes;
        var permanent = permanentResult.Codes;
        var dtcServicesRead = (storedResult.IsAvailable ? 1 : 0) + (pendingResult.IsAvailable ? 1 : 0) + (permanentResult.IsAvailable ? 1 : 0);
        ReadinessInfo? readiness = null; string mode05 = string.Empty; string mode06 = string.Empty;

        progress?.Report(new ModuleScanProgress(60, "MIL / Readiness სტატუსის წაკითხვა…"));
        var readinessRead = true;
        try { readiness = await _obd.ReadReadinessAsync(token); }
        catch (OperationCanceledException) { throw; }
        catch { readinessRead = false; notes.Add("MIL / Readiness მონაცემი მიუწვდომელია."); }
        // Mode 05/06 and multiple Mode 09 identifiers are optional and can
        // each time out on inexpensive adapters. They have their own pages;
        // keep the full scan responsive by not probing them here.
        var identifiers = Array.Empty<EcuIdentifier>();

        progress?.Report(new ModuleScanProgress(72, includeExpandedSensors
            ? "ECU-ს მხარდაჭერილი სენსორების წაკითხვა…"
            : "სწრაფი ECU live-data snapshot-ის წაკითხვა…"));
        IReadOnlyList<SensorValue> sensors;
        PidScanStats sensorStats;
        int requestedSensorCount;
        if (includeExpandedSensors)
        {
            var sensorProgress = new Progress<SensorScanProgress>(sensor =>
            {
                var fraction = sensor.Total <= 0 ? 1d : (double)sensor.Completed / sensor.Total;
                progress?.Report(new ModuleScanProgress(
                    72 + (int)Math.Round(fraction * 14d),
                    $"სენსორი: {sensor.SensorName}"));
            });
            var expanded = await _obd.ReadExpandedSupportedSensorValuesAsync(sensorProgress, token)
                .ConfigureAwait(false);
            sensors = expanded.Values;
            sensorStats = expanded.Stats;
            requestedSensorCount = expanded.Stats.Advertised;
        }
        else
        {
            var snapshotPids = ModuleSnapshotPids.Where(_obd.IsPidSupported).ToArray();
            sensors = await _obd.ReadLiveValuesAsync(snapshotPids, token).ConfigureAwait(false);
            sensorStats = new PidScanStats
            {
                Advertised = _obd.SupportedPids.Count,
                Decoded = sensors.Count,
                Failed = Math.Max(0, snapshotPids.Length - sensors.Count),
                UnsupportedDecoder = 0
            };
            requestedSensorCount = snapshotPids.Length;
        }

        progress?.Report(new ModuleScanProgress(88, "ECU პასუხის და მიღებული მონაცემების შეჯამება…"));
        var mode05Tests = AdvancedModeParser.ParseMode05(mode05);
        var mode06Tests = AdvancedModeParser.ParseMode06(mode06);
        var voltage = sensors.FirstOrDefault(x => x.Pid == 0x42);
        if (voltage != null && double.TryParse(voltage.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vb) && vb < 11.8)
            notes.Add($"Low adapter/vehicle voltage detected: {vb:0.00} V. Avoid service/write operations until stable power is confirmed.");
        var freezeFrames = new List<FreezeFrame>();
        var freezeTrigger = stored.Concat(pending).Concat(permanent).FirstOrDefault()?.Code;

        // Always ask the ECU for its Freeze Frame (Mode 02): PID 02 names the code that caused the snapshot. Some
        // ECUs / adapters answer that but not Mode 03/07/0A, which used to leave the code visible only on the
        // Freeze Frame page and missing from the trouble-code scan.
        progress?.Report(new ModuleScanProgress(94, "Freeze Frame-ის არსებობის შემოწმება…"));
        try
        {
            var frame = await _obd.ReadFreezeFrameQuickAsync(freezeTrigger, token);
            if (frame.Values.Count > 0) freezeFrames.Add(frame);

            if (frame.Values.TryGetValue("Freeze-frame DTC", out var frozenCode) &&
                !string.IsNullOrWhiteSpace(frozenCode) &&
                !stored.Concat(pending).Concat(permanent).Any(code => code.Code.Equals(frozenCode, StringComparison.OrdinalIgnoreCase)) &&
                TryDecodeCode(frozenCode, out var decoded))
            {
                stored.Add(decoded);
                notes.Add($"{frozenCode} was reported only by the ECU's Freeze Frame.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            notes.Add($"Freeze frame: {UserError.Describe(ex)}");
        }
        string? odo = null;

        // Other modules: ABS, airbag, body, steering, transmission … (read-only).
        progress?.Report(new ModuleScanProgress(95, "მოდულების შემოწმება (ABS, აირბაგი…)…"));
        ModuleDtcScanService.ScanOutcome modules;
        try
        {
            var moduleProgress = new Progress<(double Fraction, string Text)>(step =>
                progress?.Report(new ModuleScanProgress(95 + (int)Math.Floor(step.Fraction * 4d), step.Text)));
            modules = await _modules.ScanAsync(vehicle, ReadSpeedKph(sensors), moduleProgress, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            notes.Add($"Modules: {UserError.Describe(ex)}");
            modules = new ModuleDtcScanService.ScanOutcome(Array.Empty<ModuleDtcReport>(), 0,
                "სხვა მოდულების შემოწმება ვერ მოხერხდა.");
        }

        progress?.Report(new ModuleScanProgress(100, "Generic OBD-II scan დასრულდა."));
        var finished = DateTime.Now;
        var report = new ObdScanReport
        {
            StartedAt = started,
            FinishedAt = finished,
            Vehicle = vehicle,
            Protocol = _obd.CurrentProtocolDescription,
            AdapterName = adapter.AdapterName,
            AdapterVoltage = adapter.Voltage,
            EcuResponded = adapter.VehicleResponding,
            DtcServicesRead = dtcServicesRead,
            StoredDtcRead = storedResult.IsAvailable,
            PendingDtcRead = pendingResult.IsAvailable,
            PermanentDtcRead = permanentResult.IsAvailable,
            VehicleInfoRead = vehicleInfoRead,
            ReadinessRead = readinessRead,
            SnapshotPidsRequested = requestedSensorCount,
            SupportedPids = _obd.SupportedPids.ToArray(),
            PidStats = sensorStats,
            StoredCodes = stored,
            PendingCodes = pending,
            PermanentCodes = permanent,
            Readiness = readiness,
            Mode05Raw = mode05,
            Mode06Raw = mode06,
            Mode05Summary = mode05Tests.Count == 0 ? "Unavailable / no decodable tests" : $"{mode05Tests.Count} raw test items",
            Mode06Summary = mode06Tests.Count == 0 ? "Unavailable / no decodable tests" : $"{mode06Tests.Count} raw test items",
            Mode05Tests = mode05Tests,
            Mode06Tests = mode06Tests,
            Sensors = sensors,
            Identifiers = identifiers,
            OdometerKm = odo,
            FreezeFrames = freezeFrames,
            Modules = modules.Modules.ToList(),
            ModulesChecked = modules.ModulesChecked,
            ModuleScanNote = modules.Note,
            Notes = "Generic OBD-II scan. Manufacturer-specific modules require supported OEM profiles. " + adapter.OverallStatus + (voltage == null ? " Battery voltage: unavailable." : $" Battery voltage: {voltage.Value} {voltage.Unit}.") + (notes.Count == 0 ? string.Empty : " | " + string.Join(" | ", notes))
        };

        token.ThrowIfCancellationRequested();
        await PersistAsync(report);
        return report;
    }

    private static double? ReadSpeedKph(IReadOnlyList<SensorValue> sensors)
    {
        var speed = sensors.FirstOrDefault(sensor => sensor.Pid == 0x0D);
        if (speed is null)
            return null;

        var text = string.IsNullOrWhiteSpace(speed.CanonicalValue) ? speed.Value : speed.CanonicalValue;
        return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>Turns "P0238" back into a DiagnosticCode (description from the standard decoder).</summary>
    private static bool TryDecodeCode(string code, out DiagnosticCode decoded)
    {
        decoded = null!;
        code = code.Trim().ToUpperInvariant();
        if (code.Length != 5)
            return false;

        var family = code[0] switch { 'P' => 0, 'C' => 1, 'B' => 2, 'U' => 3, _ => -1 };
        if (family < 0 || !int.TryParse(code.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var number))
            return false;

        var high = (byte)((family << 6) | ((number >> 12) & 0x03) << 4 | ((number >> 8) & 0x0F));
        var low = (byte)(number & 0xFF);
        decoded = new DTCDecoder().DecodeSingle(high, low, DiagnosticCodeStatus.Stored);
        return true;
    }

    /// <summary>"NO DATA" means no ECU answered; it is not a reply that lists zero codes.</summary>
    private static (List<DiagnosticCode> Codes, bool IsAvailable) RequireReply(
        (List<DiagnosticCode> Codes, bool IsAvailable) result, bool noReply, string label, ICollection<string> notes)
    {
        if (!result.IsAvailable || !noReply) return result;
        notes.Add($"{label}: the ECU did not reply (NO DATA) — this is not the same as 'no codes'.");
        return (new List<DiagnosticCode>(), false);
    }

    private static async Task<(List<DiagnosticCode> Codes, bool IsAvailable)> ReadDtcSafelyAsync(
        Func<Task<List<DiagnosticCode>>> read,
        string label,
        ICollection<string> notes)
    {
        try { return (await read(), true); }
        catch (OperationCanceledException) { throw; }
        catch { notes.Add($"{label} მონაცემი მიუწვდომელია."); return (new List<DiagnosticCode>(), false); }
    }

    private async Task PersistAsync(ObdScanReport report)
    {
        var session = new ScanSession
        {
            StartedAt = report.StartedAt,
            FinishedAt = report.FinishedAt,
            VIN = report.Vehicle.VIN ?? string.Empty,
            VehicleSummary = $"{report.Vehicle.Manufacturer} {report.Vehicle.Model} {report.Vehicle.Year}",
            Protocol = report.Protocol,
            StoredDtcCount = report.StoredCodes.Count,
            PendingDtcCount = report.PendingCodes.Count,
            PermanentDtcCount = report.PermanentCodes.Count,
            MilOn = report.Readiness?.MilOn ?? false,
            ReportJson = JsonSerializer.Serialize(report)
        };
        await _db.SaveScanSessionAsync(session);
        foreach (var code in report.StoredCodes.Concat(report.PendingCodes).Concat(report.PermanentCodes))
            await _db.SaveScanDtcAsync(new ScanDtc { SessionId = session.Id, Code = code.Code, Status = code.Status.ToString(), Description = code.Description });
        foreach (var module in report.Modules)
        foreach (var code in module.Codes)
            await _db.SaveScanDtcAsync(new ScanDtc
            {
                SessionId = session.Id,
                Code = code.DisplayCode,
                Status = $"{module.NameEn} ({(code.Active ? "Active" : code.Pending ? "Pending" : "Stored")})",
                Description = string.IsNullOrWhiteSpace(code.TitleEn) ? code.TitleKa : code.TitleEn
            });
        foreach (var sensor in report.Sensors)
            await _db.SaveScanSensorAsync(new ScanSensor { SessionId = session.Id, Pid = sensor.Pid, Name = sensor.Name, Value = sensor.Value, Unit = sensor.Unit, Status = sensor.StatusText });
        if (report.Readiness is not null)
            await _db.SaveScanReadinessAsync(new ScanReadiness { SessionId = session.Id, MilOn = report.Readiness.MilOn, StoredDtcCount = report.Readiness.StoredDtcCount, StatusText = report.Readiness.StatusText, RawHex = report.Readiness.RawHex });
        foreach (var identifier in report.Identifiers)
            await _db.SaveScanEcuIdentifierAsync(new ScanEcuIdentifier { SessionId = session.Id, Name = identifier.Name, Value = identifier.Value });
        foreach (var ff in report.FreezeFrames)
            await _db.SaveScanFreezeFrameAsync(new ScanFreezeFrame { SessionId = session.Id, TriggerCode = ff.TriggerCode, ValuesJson = JsonSerializer.Serialize(ff.Values) });
    }
}
