using System.Globalization;
using System.Text;
using CarScanner.Database;
using CarScanner.Models;
using CarScanner.Services.Mileage;
using CarScanner.VinCheck.Services;

namespace CarScanner.Services;

/// <summary>
/// Pre-purchase check: one full, read-only inspection of a car before buying
/// it. It reuses the existing readers (full OBD + module scan, mileage
/// sources, VIN history, last 0–100 run) and turns their results into a score,
/// a verdict and short findings. Nothing is written to the car.
/// </summary>
public sealed class PrePurchaseCheckService
{
    private readonly FullObdScanService _scan;
    private readonly ActualMileageService _mileage;
    private readonly IVinHistoryService _vinHistory;
    private readonly DtcCatalogService _catalog;
    private readonly AppDatabase _db;

    public PrePurchaseCheckService(
        FullObdScanService scan,
        ActualMileageService mileage,
        IVinHistoryService vinHistory,
        DtcCatalogService catalog,
        AppDatabase db)
    {
        _scan = scan;
        _mileage = mileage;
        _vinHistory = vinHistory;
        _catalog = catalog;
        _db = db;
    }

    /// <summary>Result of the last completed check (used by the AI consultation page).</summary>
    public PrePurchaseResult? LastResult { get; private set; }

    /// <summary>False while only sample VIN data is available (no real history provider).</summary>
    public bool VinHistoryAvailable => _vinHistory is not MockVinHistoryService;

    private const string Green = "#4ADE80";
    private const string Yellow = "#FBBF24";
    private const string Orange = "#FB923C";
    private const string Red = "#F87171";
    private const string Grey = "#94A3B8";

    public async Task<PrePurchaseResult> RunAsync(
        IProgress<(int Percent, string Detail)>? progress,
        CancellationToken token)
    {
        // 1) Full OBD scan: engine codes, readiness, live values, all modules (0–80 %).
        var scanProgress = new Progress<ModuleScanProgress>(step =>
            progress?.Report(((int)Math.Round(step.Percent * 0.8), step.Stage)));
        var report = await _scan.ScanAsync(includeExpandedSensors: true, scanProgress, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        if (!report.EcuResponded)
            throw new InvalidOperationException("NO_ECU");

        var result = new PrePurchaseResult { Report = report, FinishedAt = DateTime.Now };
        var penalty = 0;

        // 2) Fault codes of the engine and of every module.
        penalty += await AddFaultsAsync(result, report, token).ConfigureAwait(false);

        // 3) Mileage from every source that has it (80–90 %).
        progress?.Report((82, string.Empty));
        ActualMileageResult? mileage = null;
        try
        {
            mileage = await _mileage.ScanAsync(report.Vehicle, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Mileage is optional; the rest of the check continues.
        }
        penalty += AddMileage(result, mileage);

        // 4) Condition: battery, temperature, fuel trims, readiness, recently cleared codes.
        penalty += AddCondition(result, report);

        // 5) VIN history (90–98 %), only with a real history provider.
        progress?.Report((90, string.Empty));
        penalty += await AddHistoryAsync(result, report.Vehicle.VIN, token).ConfigureAwait(false);

        // 6) Last 0–100 measurement of this car (optional, last 24 hours).
        await AddAccelerationAsync(result, report.Vehicle.VIN).ConfigureAwait(false);

        // Score and verdict.
        result.Score = Math.Clamp(100 - penalty, 0, 100);
        (result.VerdictKa, result.VerdictEn, result.Color) = result.Score switch
        {
            >= 85 => ("კარგი მდგომარეობა", "Good condition", Green),
            >= 70 => ("კარგი, მცირე შენიშვნებით", "Good, with minor remarks", Yellow),
            >= 50 => ("საჭიროებს ყურადღებას", "Needs attention", Orange),
            _ => ("სერიოზული პრობლემები", "Serious problems", Red)
        };

        var faultCount = result.Faults.Count(finding => finding.Color != Green);
        result.SummaryKa = string.Join(" · ", new[]
        {
            faultCount == 0 ? "შეცდომა არ არის" : $"{faultCount} შეცდომა",
            string.IsNullOrWhiteSpace(result.MileageBadgeKa) ? null : $"გარბენი: {result.MileageBadgeKa}",
            result.Condition.Any(item => item.Color is Red or Orange or Yellow) ? "მდგომარეობაში შენიშვნებია" : "მდგომარეობა ნორმაშია"
        }.Where(part => part is not null));
        result.SummaryEn = string.Join(" · ", new[]
        {
            faultCount == 0 ? "no fault codes" : $"{faultCount} fault code(s)",
            string.IsNullOrWhiteSpace(result.MileageBadgeEn) ? null : $"mileage: {result.MileageBadgeEn}",
            result.Condition.Any(item => item.Color is Red or Orange or Yellow) ? "condition remarks" : "condition normal"
        }.Where(part => part is not null));

        result.AiContext = BuildAiContext(result);
        progress?.Report((100, string.Empty));
        LastResult = result;
        return result;
    }

    // ------------------------------------------------------------------
    // Fault codes
    // ------------------------------------------------------------------

    private async Task<int> AddFaultsAsync(PrePurchaseResult result, ObdScanReport report, CancellationToken token)
    {
        var penalty = 0;
        var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task<(string Ka, string En)> TitleAsync(string code, string fallbackEn)
        {
            try
            {
                var entry = await _catalog.GetAsync(code, token).ConfigureAwait(false);
                if (entry is not null)
                    return (FirstNonEmpty(entry.TitleKa, entry.TitleEn, fallbackEn), FirstNonEmpty(entry.TitleEn, fallbackEn));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }

            return (FirstNonEmpty(fallbackEn, "აღწერა ვერ მოიძებნა"), FirstNonEmpty(fallbackEn, "No description"));
        }

        foreach (var (code, kind) in report.StoredCodes.Select(code => (code, 0))
                     .Concat(report.PendingCodes.Select(code => (code, 1)))
                     .Concat(report.PermanentCodes.Select(code => (code, 2))))
        {
            var id = code.Code.Trim().ToUpperInvariant();
            if (!generic.Add($"{id}|{kind}"))
                continue;

            var (ka, en) = await TitleAsync(id, code.Description).ConfigureAwait(false);
            var (stateKa, stateEn, color, points) = kind switch
            {
                1 => ("მოლოდინში", "pending", Yellow, 4),
                2 => ("მუდმივი", "permanent", Red, 8),
                _ => ("აქტიური", "stored", Red, 8)
            };
            penalty += points;
            result.Faults.Add(new PrePurchaseFinding
            {
                LabelKa = $"ძრავი · {id}",
                LabelEn = $"Engine · {id}",
                ValueKa = $"{ka} ({stateKa})",
                ValueEn = $"{en} ({stateEn})",
                Color = color
            });
        }

        var genericCodes = report.StoredCodes.Concat(report.PendingCodes).Concat(report.PermanentCodes)
            .Select(code => code.Code.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var module in report.Modules.Where(module => module.Responded))
        {
            foreach (var code in module.Codes)
            {
                if (module.ModuleId == "ECM" && genericCodes.Contains(code.Code))
                    continue;

                var (ka, en) = string.IsNullOrWhiteSpace(code.TitleKa) && string.IsNullOrWhiteSpace(code.TitleEn)
                    ? await TitleAsync(code.Code, string.Empty).ConfigureAwait(false)
                    : (FirstNonEmpty(code.TitleKa, code.TitleEn), FirstNonEmpty(code.TitleEn, code.TitleKa));

                var important = module.ModuleId is "ABS" or "SRS";
                var (stateKa, stateEn, color, points) = code.Active
                    ? ("აქტიური", "active", Red, important ? 10 : 8)
                    : code.Pending
                        ? ("მოლოდინში", "pending", Yellow, 3)
                        : ("ისტორია", "history", Grey, 2);
                penalty += points;
                result.Faults.Add(new PrePurchaseFinding
                {
                    LabelKa = $"{module.NameKa} · {code.DisplayCode}",
                    LabelEn = $"{module.NameEn} · {code.DisplayCode}",
                    ValueKa = $"{ka} ({stateKa})",
                    ValueEn = $"{en} ({stateEn})",
                    Color = color
                });
            }
        }

        if (result.Faults.Count == 0)
        {
            result.Faults.Add(new PrePurchaseFinding
            {
                LabelKa = "ყველა მოდული",
                LabelEn = "All modules",
                ValueKa = "შეცდომა არ არის",
                ValueEn = "No fault codes",
                Color = Green
            });
        }

        return Math.Min(penalty, 55);
    }

    // ------------------------------------------------------------------
    // Mileage
    // ------------------------------------------------------------------

    private static int AddMileage(PrePurchaseResult result, ActualMileageResult? mileage)
    {
        if (mileage is null || (mileage.Evidence.Count == 0 && !mileage.FinalMileageKm.HasValue))
        {
            (result.MileageBadgeKa, result.MileageBadgeEn, result.MileageBadgeColor) = ("მიუწვდომელია", "not available", Grey);
            result.Mileage.Add(new PrePurchaseFinding
            {
                LabelKa = "გარბენი",
                LabelEn = "Mileage",
                ValueKa = "მანქანამ გარბენი არ დააბრუნა",
                ValueEn = "The car did not report the mileage",
                Color = Grey
            });
            return 0;
        }

        foreach (var evidence in mileage.Evidence.OrderByDescending(item => item.Confidence).Take(5))
        {
            result.Mileage.Add(new PrePurchaseFinding
            {
                LabelKa = FirstNonEmpty(evidence.Ecu, evidence.Source),
                LabelEn = FirstNonEmpty(evidence.Ecu, evidence.Source),
                ValueKa = $"{evidence.ValueKm.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ')} კმ",
                ValueEn = $"{evidence.ValueKm.ToString("N0", CultureInfo.InvariantCulture)} km",
                Color = mileage.HasMismatch ? Yellow : Green
            });
        }

        if (mileage.Evidence.Count == 0 && mileage.FinalMileageKm is { } odometer)
        {
            result.Mileage.Add(new PrePurchaseFinding
            {
                LabelKa = "ოდომეტრი",
                LabelEn = "Odometer",
                ValueKa = $"{odometer.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ')} კმ",
                ValueEn = $"{odometer.ToString("N0", CultureInfo.InvariantCulture)} km",
                Color = Green
            });
        }

        if (mileage.HasMismatch)
        {
            (result.MileageBadgeKa, result.MileageBadgeEn, result.MileageBadgeColor) = ("შეუსაბამობა", "mismatch", Red);
            var difference = mileage.MaximumDifferenceKm is { } km ? km.ToString("N0", CultureInfo.InvariantCulture) : "?";
            result.Mileage.Add(new PrePurchaseFinding
            {
                LabelKa = "სხვაობა მოდულებს შორის",
                LabelEn = "Difference between modules",
                ValueKa = $"{difference} კმ — შესაძლოა გადაგრეხილია",
                ValueEn = $"{difference} km — may have been rolled back",
                Color = Red
            });
            return 25;
        }

        if (mileage.SourceCount >= 2)
            (result.MileageBadgeKa, result.MileageBadgeEn, result.MileageBadgeColor) = ("სანდო", "consistent", Green);
        else
            (result.MileageBadgeKa, result.MileageBadgeEn, result.MileageBadgeColor) = ("ერთი წყარო", "one source", Grey);
        return 0;
    }

    // ------------------------------------------------------------------
    // Condition
    // ------------------------------------------------------------------

    private static int AddCondition(PrePurchaseResult result, ObdScanReport report)
    {
        var penalty = 0;
        var rpm = Sensor(report, 0x0C);
        var running = rpm is > 400;

        // Battery / charging.
        var voltage = Sensor(report, 0x42) ?? ParseNumber(report.AdapterVoltage);
        if (voltage is { } volts)
        {
            string ka, en, color;
            if (running)
            {
                (ka, en, color) = volts switch
                {
                    < 13.2 => ("დამუხტვა სუსტია", "weak charging", Yellow),
                    > 15.0 => ("დამუხტვა მაღალია", "charging too high", Yellow),
                    _ => ("დამუხტვა ნორმაშია", "charging OK", Green)
                };
            }
            else
            {
                (ka, en, color) = volts switch
                {
                    < 12.0 => ("ბატარეა დაცლილია", "battery low", Red),
                    < 12.4 => ("ბატარეა ნახევრად დაცლილია", "battery partly discharged", Yellow),
                    _ => ("ბატარეა კარგია", "battery good", Green)
                };
            }

            penalty += color == Red ? 8 : color == Yellow ? 4 : 0;
            result.Condition.Add(new PrePurchaseFinding
            {
                LabelKa = "ბატარეა / დამუხტვა",
                LabelEn = "Battery / charging",
                ValueKa = $"{volts:0.0} V · {ka}",
                ValueEn = $"{volts:0.0} V · {en}",
                Color = color
            });
        }

        // Engine temperature (only meaningful with the engine running).
        if (Sensor(report, 0x05) is { } coolant)
        {
            var (ka, en, color) = !running
                ? ("ძრავი არ მუშაობს", "engine not running", Grey)
                : coolant switch
                {
                    > 108 => ("გადახურება", "overheating", Red),
                    < 60 => ("ჯერ არ გამთბარა", "not warmed up yet", Grey),
                    _ => ("ნორმაშია", "normal", Green)
                };
            penalty += color == Red ? 10 : 0;
            result.Condition.Add(new PrePurchaseFinding
            {
                LabelKa = "ძრავის ტემპერატურა",
                LabelEn = "Engine temperature",
                ValueKa = $"{coolant:0} °C · {ka}",
                ValueEn = $"{coolant:0} °C · {en}",
                Color = color
            });
        }

        // Long-term fuel trims.
        var trims = new[] { Sensor(report, 0x07), Sensor(report, 0x09) }.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        if (trims.Count > 0)
        {
            var worst = trims.OrderByDescending(Math.Abs).First();
            var (ka, en, color) = Math.Abs(worst) switch
            {
                > 20 => ("ძალიან დიდია — ჰაერის ჟონვა ან საწვავის პრობლემა", "very high — air leak or fuel problem", Red),
                > 10 => ("მომატებულია", "elevated", Yellow),
                _ => ("ნორმაშია", "normal", Green)
            };
            penalty += color == Red ? 10 : color == Yellow ? 5 : 0;
            result.Condition.Add(new PrePurchaseFinding
            {
                LabelKa = "საწვავის კორექცია",
                LabelEn = "Fuel trim",
                ValueKa = $"{worst:+0.0;-0.0;0} % · {ka}",
                ValueEn = $"{worst:+0.0;-0.0;0} % · {en}",
                Color = color
            });
        }

        // Codes cleared recently: a common way to hide problems before a sale.
        var sinceClearKm = Sensor(report, 0x31);
        var sinceClearMin = Sensor(report, 0x4E);
        if (sinceClearKm is < 100 || sinceClearMin is < 300)
        {
            penalty += 6;
            var detailKa = sinceClearKm is { } km ? $"{km:0} კმ-ის წინ" : $"{sinceClearMin:0} წთ-ის წინ";
            var detailEn = sinceClearKm is { } km2 ? $"{km2:0} km ago" : $"{sinceClearMin:0} min ago";
            result.Condition.Add(new PrePurchaseFinding
            {
                LabelKa = "შეცდომები წაშლილია",
                LabelEn = "Codes were cleared",
                ValueKa = $"{detailKa} — გადაამოწმე რატომ",
                ValueEn = $"{detailEn} — ask why",
                Color = Yellow
            });
        }

        // Emission readiness ("ready for the technical inspection").
        if (report.Readiness is { RawBytes.Length: >= 4 } readiness)
        {
            var (ready, total) = CountReadiness(readiness.RawBytes);
            var incomplete = total - ready;
            var ok = !readiness.MilOn && incomplete <= 1;
            if (!ok)
                penalty += readiness.MilOn ? 5 : 4;
            result.Condition.Add(new PrePurchaseFinding
            {
                LabelKa = "ტექდათვალიერება",
                LabelEn = "Inspection readiness",
                ValueKa = ok
                    ? $"მზადაა ({ready}/{total})"
                    : readiness.MilOn
                        ? "Check Engine ანთია"
                        : $"არ არის მზად ({ready}/{total})",
                ValueEn = ok
                    ? $"ready ({ready}/{total})"
                    : readiness.MilOn
                        ? "Check Engine is on"
                        : $"not ready ({ready}/{total})",
                Color = ok ? Green : Yellow
            });
        }

        return penalty;
    }

    private static (int Ready, int Total) CountReadiness(byte[] data)
    {
        var b = data[1];
        var c = data[2];
        var d = data[3];
        var ready = 0;
        var total = 0;
        for (var bit = 0; bit < 3; bit++)
        {
            if ((b & (1 << bit)) == 0) continue;
            total++;
            if ((b & (1 << (bit + 4))) == 0) ready++;
        }

        for (var bit = 0; bit < 8; bit++)
        {
            if ((c & (1 << bit)) == 0) continue;
            total++;
            if ((d & (1 << bit)) == 0) ready++;
        }

        return (ready, total);
    }

    // ------------------------------------------------------------------
    // VIN history / acceleration
    // ------------------------------------------------------------------

    private async Task<int> AddHistoryAsync(PrePurchaseResult result, string? vin, CancellationToken token)
    {
        if (!VinHistoryAvailable || string.IsNullOrWhiteSpace(vin) || vin.Trim().Length != 17)
        {
            result.History.Add(new PrePurchaseFinding
            {
                LabelKa = "VIN ისტორია",
                LabelEn = "VIN history",
                ValueKa = string.IsNullOrWhiteSpace(vin) || vin.Trim().Length != 17 ? "VIN ვერ წაიკითხა" : "ჯერ არ არის ჩართული",
                ValueEn = string.IsNullOrWhiteSpace(vin) || vin.Trim().Length != 17 ? "VIN could not be read" : "not enabled yet",
                Color = Grey
            });
            return 0;
        }

        try
        {
            var history = await _vinHistory.GetReportAsync(vin.Trim().ToUpperInvariant(), token).ConfigureAwait(false);
            var issues = history.Alerts.Where(alert => alert.Status == "issue").ToList();
            var accidents = history.AccidentRecords.Count;
            if (issues.Count == 0 && accidents == 0)
            {
                result.History.Add(new PrePurchaseFinding
                {
                    LabelKa = "VIN ისტორია",
                    LabelEn = "VIN history",
                    ValueKa = "პრობლემური ჩანაწერი არ არის",
                    ValueEn = "no problem records",
                    Color = Green
                });
                return 0;
            }

            foreach (var alert in issues.Take(4))
            {
                result.History.Add(new PrePurchaseFinding
                {
                    LabelKa = $"{VinText.T(alert.Label)}",
                    LabelEn = $"{alert.Label}",
                    ValueKa = VinText.T(alert.Detail),
                    ValueEn = alert.Detail,
                    Color = Yellow
                });
            }

            if (issues.Count == 0 && accidents > 0)
            {
                result.History.Add(new PrePurchaseFinding
                {
                    LabelKa = "ავარიები",
                    LabelEn = "Accidents",
                    ValueKa = $"{accidents} ჩანაწერი",
                    ValueEn = $"{accidents} record(s)",
                    Color = Yellow
                });
            }

            return Math.Min(24, Math.Max(issues.Count, accidents > 0 ? 1 : 0) * 8);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            result.History.Add(new PrePurchaseFinding
            {
                LabelKa = "VIN ისტორია",
                LabelEn = "VIN history",
                ValueKa = "ვერ ჩაიტვირთა (ინტერნეტი?)",
                ValueEn = "could not be loaded (internet?)",
                Color = Grey
            });
            return 0;
        }
    }

    private async Task AddAccelerationAsync(PrePurchaseResult result, string? vin)
    {
        try
        {
            var run = await _db.GetLatestAccelerationRunAsync().ConfigureAwait(false);
            if (run is null || run.FinishedAt < DateTime.Now.AddHours(-24) ||
                (!string.IsNullOrWhiteSpace(vin) && !string.IsNullOrWhiteSpace(run.VIN) &&
                 !string.Equals(run.VIN, vin, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            result.Condition.Add(new PrePurchaseFinding
            {
                LabelKa = "0–100 კმ/სთ",
                LabelEn = "0–100 km/h",
                ValueKa = $"{run.DurationSeconds:0.0} წმ",
                ValueEn = $"{run.DurationSeconds:0.0} s",
                Color = Green
            });
        }
        catch
        {
        }
    }

    // ------------------------------------------------------------------
    // AI context
    // ------------------------------------------------------------------

    private static string BuildAiContext(PrePurchaseResult result)
    {
        var report = result.Report;
        var sb = new StringBuilder();
        sb.AppendLine("===== PRE-PURCHASE CHECK RESULT (read-only scan done by GCar360 just now) =====");
        sb.AppendLine($"Vehicle: {report.Vehicle.Manufacturer} {report.Vehicle.Model} {report.Vehicle.Year} · VIN {report.Vehicle.VIN}");
        sb.AppendLine($"Engine: {report.Vehicle.Engine} · Fuel: {report.Vehicle.FuelType}");
        sb.AppendLine($"Overall score: {result.Score}/100 ({result.VerdictEn})");
        sb.AppendLine($"Modules that answered: {report.Modules.Count(module => module.Responded)} of {report.ModulesChecked} addresses checked.");

        void Section(string title, IEnumerable<PrePurchaseFinding> items)
        {
            sb.AppendLine($"{title}:");
            foreach (var item in items)
                sb.AppendLine($"- {item.LabelEn}: {item.ValueEn}");
        }

        Section("Fault codes", result.Faults);
        Section($"Mileage ({result.MileageBadgeEn})", result.Mileage);
        Section("Condition", result.Condition);
        Section("VIN history", result.History);

        var sensors = report.Sensors.Take(40).Select(sensor =>
            $"{sensor.Name}: {FirstNonEmpty(sensor.CanonicalValue, sensor.Value)} {sensor.Unit}");
        sb.AppendLine("Live values at the time of the scan: " + string.Join("; ", sensors));
        sb.AppendLine("===== END OF CHECK RESULT =====");
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static double? Sensor(ObdScanReport report, int pid)
    {
        var sensor = report.Sensors.FirstOrDefault(item => item.Pid == pid);
        if (sensor is null)
            return null;
        return ParseNumber(FirstNonEmpty(sensor.CanonicalValue, sensor.Value));
    }

    private static double? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var cleaned = new string(text.Trim().TakeWhile(ch => char.IsDigit(ch) || ch is '.' or '-' or ',').ToArray()).Replace(',', '.');
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) &&
                                       !string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase))?.Trim() ?? string.Empty;
}
