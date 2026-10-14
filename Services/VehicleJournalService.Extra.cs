using System.Globalization;
using System.Text;
using System.Text.Json;
using CarScanner.Models;

namespace CarScanner.Services;

/// <summary>
/// Extra report sections that go beyond fuel: driving style (from saved live recordings), battery and temperature
/// history, recurring fault codes, ECU passport, recordings and mileage checks. Every line comes from stored data.
/// </summary>
public sealed partial class VehicleJournalService
{
    private sealed record SavedScan(ScanSession Session, ObdScanReport Report);

    private sealed class DrivingStats
    {
        public int Recordings;
        public double Km;
        public double MovingSeconds;
        public double IdleSeconds;
        public int HardAccel;
        public int HardBrake;
        public double MaxRpm;
        public double RpmSeconds;
        public double HighRevSeconds;
        public double MaxSpeed;
        public double? MaxCoolant;
        public double? MinVoltage;
        public double LoadSum;
        public int LoadCount;
        public double ThrottleSum;
        public int ThrottleCount;
    }

    // ------------------------------------------------------------------ loading

    private static List<SavedScan> LoadReports(IEnumerable<ScanSession> sessions)
    {
        var list = new List<SavedScan>();
        foreach (var session in sessions.OrderByDescending(s => s.StartedAt).Take(30))
        {
            if (string.IsNullOrWhiteSpace(session.ReportJson))
                continue;

            try
            {
                var report = JsonSerializer.Deserialize<ObdScanReport>(session.ReportJson);
                if (report is not null)
                    list.Add(new SavedScan(session, report));
            }
            catch
            {
                // Old or incomplete saved scan: skip it instead of failing the whole report.
            }
        }

        return list;
    }

    private async Task<DrivingStats?> AnalyzeRecordingsAsync(string? vin)
    {
        IReadOnlyList<RecordingSession> sessions;
        try { sessions = await _recordings.GetSessionsAsync(); }
        catch { return null; }

        var chosen = sessions
            .Where(s => vin is null || string.IsNullOrWhiteSpace(s.Vin) || s.Vin.Equals(vin, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();

        var stats = new DrivingStats();

        foreach (var session in chosen)
        {
            RecordingSessionData? data;
            try { data = await _recordings.LoadAsync(session); }
            catch { continue; }

            if (data is null || data.Samples.Count < 20)
                continue;

            stats.Recordings++;
            Accumulate(stats, data.Samples);
        }

        return stats.Recordings == 0 ? null : stats;
    }

    private static void Accumulate(DrivingStats stats, IReadOnlyList<ObdTelemetrySample> samples)
    {
        var speed = samples.Where(s => s.Pid == 0x0D).OrderBy(s => s.Timestamp).ToList();
        var rpm = samples.Where(s => s.Pid == 0x0C).OrderBy(s => s.Timestamp).ToList();

        var wasHardAccel = false;
        var wasHardBrake = false;

        for (var i = 1; i < speed.Count; i++)
        {
            var dt = (speed[i].Timestamp - speed[i - 1].Timestamp).TotalSeconds;
            if (dt <= 0 || dt > 5)
            {
                wasHardAccel = wasHardBrake = false;
                continue;
            }

            var v0 = speed[i - 1].Value;
            var v1 = speed[i].Value;
            stats.Km += (v0 + v1) / 2d * dt / 3600d;
            stats.MaxSpeed = Math.Max(stats.MaxSpeed, v1);

            if (v0 < 1 && v1 < 1) stats.IdleSeconds += dt;
            else stats.MovingSeconds += dt;

            if (dt is >= 0.15 and <= 3)
            {
                var a = (v1 - v0) / 3.6d / dt;

                var hardAccel = a >= 2.5 && v1 > 5;
                if (hardAccel && !wasHardAccel) stats.HardAccel++;
                wasHardAccel = hardAccel;

                var hardBrake = a <= -3.0 && v0 > 10;
                if (hardBrake && !wasHardBrake) stats.HardBrake++;
                wasHardBrake = hardBrake;
            }
        }

        for (var i = 1; i < rpm.Count; i++)
        {
            var dt = (rpm[i].Timestamp - rpm[i - 1].Timestamp).TotalSeconds;
            if (dt <= 0 || dt > 3)
                continue;

            stats.RpmSeconds += dt;
            if (rpm[i].Value >= 4000) stats.HighRevSeconds += dt;
        }

        if (rpm.Count > 0) stats.MaxRpm = Math.Max(stats.MaxRpm, rpm.Max(s => s.Value));

        foreach (var s in samples.Where(s => s.Pid == 0x04)) { stats.LoadSum += s.Value; stats.LoadCount++; }
        foreach (var s in samples.Where(s => s.Pid == 0x11)) { stats.ThrottleSum += s.Value; stats.ThrottleCount++; }

        var coolant = samples.Where(s => s.Pid == 0x05 && s.Value is > -40 and < 160).Select(s => s.Value).ToList();
        if (coolant.Count > 0) stats.MaxCoolant = Math.Max(stats.MaxCoolant ?? double.MinValue, coolant.Max());

        var volts = samples.Where(s => s.Pid == 0x42 && s.Value is > 5 and < 20).Select(s => s.Value).ToList();
        if (volts.Count > 0) stats.MinVoltage = Math.Min(stats.MinVoltage ?? double.MaxValue, volts.Min());
    }

    // ------------------------------------------------------------------ sections

    private void AppendHighlights(StringBuilder sb, ObdScanReport? ecu, List<FuelTripRecord> trips, List<ScanSession> scans, DrivingStats? driving, DateTime now)
    {
        var lines = new List<string>();

        if (ecu is { EcuResponded: true })
        {
            var codes = ecu.StoredCodes.Count + ecu.PendingCodes.Count + ecu.PermanentCodes.Count + ecu.Modules.Sum(m => m.Codes.Count);
            lines.Add(codes == 0 && ecu.Readiness?.MilOn != true
                ? T("ECU: პრობლემა არ ჩანს", "ECU: no problems")
                : T($"ECU: {codes} კოდი" + (ecu.Readiness?.MilOn == true ? ", MIL ანთია" : string.Empty),
                    $"ECU: {codes} code(s)" + (ecu.Readiness?.MilOn == true ? ", MIL on" : string.Empty)));
        }

        var week = trips.Where(t => t.StartedAt >= now.AddDays(-7)).ToList();
        if (week.Count > 0)
            lines.Add(T($"ბოლო 7 დღე: {week.Count} მგზავრობა · {Dist(week.Sum(t => t.DistanceKm))}",
                $"Last 7 days: {week.Count} trips · {Dist(week.Sum(t => t.DistanceKm))}"));

        if (trips.Count > 0)
            lines.Add(T($"სულ ჩაწერილი: {trips.Count} მგზავრობა · {Dist(trips.Sum(t => t.DistanceKm))}",
                $"Recorded in total: {trips.Count} trips · {Dist(trips.Sum(t => t.DistanceKm))}"));

        if (driving is { Km: >= 5 })
            lines.Add(T("მართვის სტილი: ", "Driving style: ") + StyleLabel(driving));

        if (scans.Count > 0)
            lines.Add(T("ბოლო სკანი: ", "Last scan: ") + Ago(now, scans.Max(s => s.StartedAt)));

        if (lines.Count == 0)
            return;

        sb.AppendLine("▌ " + T("მოკლედ", "At a glance"));
        foreach (var line in lines)
            sb.AppendLine("• " + line);
        sb.AppendLine();
    }

    private string StyleLabel(DrivingStats d)
    {
        var per100 = (d.HardAccel + d.HardBrake) / d.Km * 100d;
        return per100 switch
        {
            < 8 => T("მშვიდი", "calm"),
            < 25 => T("ზომიერი", "moderate"),
            _ => T("აქტიური", "spirited")
        };
    }

    private void AppendDrivingStyle(StringBuilder sb, DrivingStats? d)
    {
        if (d is null || d.Km < 2)
            return;

        sb.AppendLine("▌ " + T("მართვის სტილი", "Driving style") + $" ({d.Recordings} " + T("ჩანაწერი", "recordings") + ")");
        sb.AppendLine($"• {T("შეფასება", "Rating")}: {StyleLabel(d)} ({T("ორიენტაციური", "indicative")})");
        sb.AppendLine($"• {T("მკვეთრი დაჩქარება", "Hard accelerations")}: {d.HardAccel} · {T("მკვეთრი დამუხრუჭება", "hard braking")}: {d.HardBrake} ({T("100 კმ-ზე", "per 100 km")}: {(d.HardAccel + d.HardBrake) / d.Km * 100d:0})");

        if (d.RpmSeconds > 30)
        {
            sb.AppendLine($"• {T("მაქსიმალური ბრუნვები", "Peak RPM")}: {d.MaxRpm:0}");
            sb.AppendLine($"• {T("დრო 4000 RPM-ზე მაღლა", "Time above 4000 RPM")}: {d.HighRevSeconds / d.RpmSeconds * 100d:0.#}%");
        }

        if (d.MovingSeconds + d.IdleSeconds > 60)
            sb.AppendLine($"• {T("გაჩერებული/მოძრავი", "Stopped / moving")}: {d.IdleSeconds / (d.IdleSeconds + d.MovingSeconds) * 100d:0}% / {d.MovingSeconds / (d.IdleSeconds + d.MovingSeconds) * 100d:0}%");

        if (d.LoadCount > 0)
            sb.AppendLine($"• {T("საშუალო დატვირთვა", "Average engine load")}: {d.LoadSum / d.LoadCount:0}%");
        if (d.ThrottleCount > 0)
            sb.AppendLine($"• {T("საშუალო დროსელი", "Average throttle")}: {d.ThrottleSum / d.ThrottleCount:0}%");
        if (d.MaxSpeed > 0)
            sb.AppendLine($"• {T("მაქს. სიჩქარე ჩანაწერებში", "Top speed in recordings")}: {_units.FormatSpeed(d.MaxSpeed)}");

        sb.AppendLine();
    }

    private void AppendBodyHealth(StringBuilder sb, List<SavedScan> scans, DrivingStats? d)
    {
        var volts = new List<double>();
        var coolant = new List<double>();
        var idleRpm = new List<double>();

        foreach (var scan in scans)
        {
            var speed = SensorNumber(scan.Report, 0x0D);
            var rpm = SensorNumber(scan.Report, 0x0C);
            var v = SensorNumber(scan.Report, 0x42);
            var c = SensorNumber(scan.Report, 0x05);

            if (v is > 5 and < 20) volts.Add(v.Value);
            if (c is > -40 and < 160) coolant.Add(c.Value);
            if (speed is < 1 && rpm is > 300 and < 2500) idleRpm.Add(rpm.Value);
        }

        if (volts.Count == 0 && coolant.Count == 0 && idleRpm.Count == 0 && d?.MaxCoolant is null && d?.MinVoltage is null)
            return;

        sb.AppendLine("▌ " + T("აკუმულატორი, ტემპერატურა, ძრავა", "Battery, temperature, engine"));

        if (volts.Count > 0)
        {
            sb.AppendLine($"• {T("ძაბვა (სკანებზე)", "Voltage (scans)")}: {T("ბოლო", "last")} {volts[0]:0.0} V · {T("დიაპაზონი", "range")} {volts.Min():0.0}–{volts.Max():0.0} V ({volts.Count})");
            var low = volts.Count(x => x < 12.0);
            if (low > 0)
                sb.AppendLine($"• {T($"{low} სკანზე ძაბვა 12.0 V-ზე დაბალი იყო (ძრავა ჩაქრობილზე ეს ნორმალურია)", $"Voltage was below 12.0 V in {low} scan(s) (normal with the engine off)")}");
        }

        if (d?.MinVoltage is not null)
            sb.AppendLine($"• {T("მინიმალური ძაბვა ჩანაწერებში", "Lowest voltage in recordings")}: {d.MinVoltage:0.0} V");

        if (coolant.Count > 0)
            sb.AppendLine($"• {T("გაგრილების სითხე (სკანებზე)", "Coolant (scans)")}: {T("ბოლო", "last")} {_units.FormatTemperature(coolant[0])} · {T("მაქს.", "max")} {_units.FormatTemperature(coolant.Max())}");

        if (d?.MaxCoolant is not null)
            sb.AppendLine($"• {T("მაქს. ტემპერატურა ჩანაწერებში", "Peak coolant temperature in recordings")}: {_units.FormatTemperature(d.MaxCoolant.Value)}");

        if (idleRpm.Count > 0)
        {
            sb.AppendLine($"• {T("უქმი ბრუნვები", "Idle RPM")}: {idleRpm.Average():0} ({idleRpm.Min():0}–{idleRpm.Max():0}, {idleRpm.Count})");
            if (idleRpm.Count >= 3 && idleRpm.Max() - idleRpm.Min() > 250)
                sb.AppendLine("• " + T("უქმი ბრუნვები ერთმანეთისგან მკვეთრად განსხვავდება — შეამოწმე დროსელი/ჰაერის გაჟონვა.",
                    "Idle RPM varies a lot between scans — check the throttle body and air leaks."));
        }

        sb.AppendLine();
    }

    private void AppendFaultHistory(StringBuilder sb, List<SavedScan> scans)
    {
        if (scans.Count == 0)
            return;

        var ordered = scans.OrderBy(s => s.Session.StartedAt).ToList();
        var lastSession = ordered[^1].Session.Id;
        var map = new Dictionary<string, (string Description, List<DateTime> Seen, bool InLast)>(StringComparer.OrdinalIgnoreCase);

        foreach (var scan in ordered)
        {
            var codes = scan.Report.StoredCodes.Concat(scan.Report.PendingCodes).Concat(scan.Report.PermanentCodes)
                .Select(c => c).GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase).Select(g => g.First());

            foreach (var code in codes)
            {
                if (!map.TryGetValue(code.Code, out var entry))
                    entry = (code.Description, new List<DateTime>(), false);

                entry.Seen.Add(scan.Session.StartedAt);
                entry.InLast = scan.Session.Id == lastSession;
                map[code.Code] = entry;
            }
        }

        if (map.Count == 0)
        {
            sb.AppendLine("▌ " + T("შეცდომების ისტორია", "Fault history"));
            sb.AppendLine("• " + T($"ბოლო {scans.Count} სკანზე შეცდომის კოდი არც ერთხელ არ დაფიქსირებულა.", $"No fault code was ever recorded in the last {scans.Count} scan(s)."));
            sb.AppendLine();
            return;
        }

        sb.AppendLine("▌ " + T("შეცდომების ისტორია", "Fault history"));
        foreach (var (code, entry) in map.OrderByDescending(e => e.Value.InLast).ThenByDescending(e => e.Value.Seen.Count).Take(8))
        {
            var state = entry.InLast ? T("ჯერ კიდევ აქტიურია", "still present") : T("ბოლო სკანზე აღარ ჩანს", "gone in the last scan");
            var recurring = entry.Seen.Count > 1 ? T($" · {entry.Seen.Count}-ჯერ", $" · seen {entry.Seen.Count}×") : string.Empty;
            var desc = string.IsNullOrWhiteSpace(entry.Description) ? string.Empty : " — " + entry.Description;
            sb.AppendLine($"• {code}{desc}{recurring} · {entry.Seen.Min():dd.MM} → {entry.Seen.Max():dd.MM} · {state}");
        }

        sb.AppendLine();
    }

    private void AppendEcuPassport(StringBuilder sb, ObdScanReport? ecu, List<SavedScan> scans)
    {
        var report = ecu is { EcuResponded: true } ? ecu : scans.OrderByDescending(s => s.Session.StartedAt).FirstOrDefault()?.Report;
        if (report is null)
            return;

        sb.AppendLine("▌ " + T("ECU პასპორტი", "ECU passport") + (ReferenceEquals(report, ecu) ? string.Empty : T(" (ბოლო სკანიდან)", " (from the last scan)")));

        if (!string.IsNullOrWhiteSpace(report.Protocol) && report.Protocol != "Unknown")
            sb.AppendLine($"• {T("პროტოკოლი", "Protocol")}: {report.Protocol}");
        if (!string.IsNullOrWhiteSpace(report.AdapterName))
            sb.AppendLine($"• {T("ადაპტერი", "Adapter")}: {report.AdapterName}" + (string.IsNullOrWhiteSpace(report.AdapterVoltage) ? string.Empty : $" · {report.AdapterVoltage}"));
        if (IsKnown(report.Vehicle.ECUName)) sb.AppendLine($"• ECU: {report.Vehicle.ECUName}");
        if (IsKnown(report.Vehicle.CalibrationId)) sb.AppendLine($"• Calibration ID: {report.Vehicle.CalibrationId}");
        if (IsKnown(report.Vehicle.CVN)) sb.AppendLine($"• CVN: {report.Vehicle.CVN}");

        if (report.SupportedPids.Count > 0)
            sb.AppendLine($"• {T("მხარდაჭერილი PID-ები", "Supported PIDs")}: {report.SupportedPids.Count}" +
                          (report.PidStats.Advertised > 0 ? $" ({T("წაკითხული", "read")} {report.PidStats.Decoded}/{Math.Max(report.PidStats.Advertised, report.PidStats.Decoded)})" : string.Empty));

        var calibrations = scans
            .Select(s => s.Report.Vehicle.CalibrationId)
            .Where(IsKnown)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (calibrations.Count > 1)
            sb.AppendLine("• " + T("Calibration ID სკანებს შორის შეიცვალა — ECU შეიძლება გადაპროგრამებული იყოს.",
                "Calibration ID changed between scans — the ECU may have been reflashed."));

        sb.AppendLine();
    }

    private async Task AppendRecordingsAndMileageAsync(StringBuilder sb, string? vin, DateTime now)
    {
        var lines = new List<string>();

        try
        {
            var recordings = (await _recordings.GetSessionsAsync())
                .Where(s => vin is null || string.IsNullOrWhiteSpace(s.Vin) || s.Vin.Equals(vin, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (recordings.Count > 0)
            {
                var total = TimeSpan.FromSeconds(recordings.Sum(r => r.Duration.TotalSeconds));
                lines.Add($"{T("ჩანაწერები", "Recordings")}: {recordings.Count} · {(int)total.TotalHours} h {total.Minutes} min · {recordings.Sum(r => r.SampleCount):N0} {T("მონაცემი", "samples")}");
                lines.Add($"{T("ბოლო ჩანაწერი", "Last recording")}: {recordings[0].StartedAt:dd.MM.yyyy HH:mm} ({Ago(now, recordings[0].StartedAt)})");
            }
        }
        catch
        {
            // Recordings are optional.
        }

        if (vin is not null)
        {
            try
            {
                var checks = await _db.GetMileageAsync(vin);
                if (checks.Count > 0)
                {
                    var last = checks[0];
                    lines.Add($"{T("გარბენის შემოწმებები", "Mileage checks")}: {checks.Count} · {T("ბოლო", "last")} {last.CheckedAt:dd.MM.yyyy}" +
                              (string.IsNullOrWhiteSpace(last.Value) ? string.Empty : $" · {last.Value}") +
                              (string.IsNullOrWhiteSpace(last.Status) ? string.Empty : $" ({last.Status})"));
                }
            }
            catch
            {
                // Optional too.
            }
        }

        if (lines.Count == 0)
            return;

        sb.AppendLine("▌ " + T("ჩანაწერები და გარბენის შემოწმება", "Recordings & mileage checks"));
        foreach (var line in lines)
            sb.AppendLine("• " + line);
        sb.AppendLine();
    }

    private static double? SensorNumber(ObdScanReport report, int pid)
    {
        var sensor = report.Sensors.FirstOrDefault(s => s.Pid == pid);
        if (sensor is null)
            return null;

        var text = string.IsNullOrWhiteSpace(sensor.CanonicalValue) ? sensor.Value : sensor.CanonicalValue;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
