using System.Globalization;
using System.Text;
using CarScanner.Database;
using CarScanner.Models;
using CarScanner.Services.Localization;

namespace CarScanner.Services;

/// <summary>
/// Builds the plain-text "vehicle report" from data the app has already stored (trips, refuels, scans,
/// acceleration runs). Nothing is estimated or invented: a line is only written when there is data behind it.
/// </summary>
public sealed partial class VehicleJournalService
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly AppDatabase _db;
    private readonly VehicleInfoService _vehicle;
    private readonly UnitSystemService _units;
    private readonly LocalizationService _localization;
    private readonly FullObdScanService _fullScan;
    private readonly IBluetoothService _bluetooth;
    private readonly RecordingSessionService _recordings;

    public VehicleJournalService(
        AppDatabase db,
        VehicleInfoService vehicle,
        UnitSystemService units,
        LocalizationService localization,
        FullObdScanService fullScan,
        IBluetoothService bluetooth,
        RecordingSessionService recordings)
    {
        _recordings = recordings;
        _fullScan = fullScan;
        _bluetooth = bluetooth;
        _db = db;
        _vehicle = vehicle;
        _units = units;
        _localization = localization;
    }

    private string T(string ka, string en) => _localization.IsEnglish ? en : ka;

    public async Task<string> BuildAsync(IProgress<string>? progress = null)
    {
        // 1) Live ECU scan first (only when the adapter is connected) so the new scan is also in the saved history below.
        ObdScanReport? ecu = null;
        string? ecuError = null;
        if (_bluetooth.IsConnected)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(75));
                var relay = new Progress<ModuleScanProgress>(p => progress?.Report($"{p.Percent}% · {p.Stage}"));
                ecu = await _fullScan.ScanAsync(relay, cts.Token);
            }
            catch (OperationCanceledException)
            {
                ecuError = T("ECU სკანი დროში ვერ დასრულდა.", "The ECU scan did not finish in time.");
            }
            catch (Exception ex)
            {
                ecuError = ex.Message;
            }
        }

        progress?.Report(T("შენახული მონაცემების დამუშავება…", "Reading saved data…"));

        var now = DateTime.Now;
        var info = ecu?.Vehicle is { } scanned && IsKnown(scanned.VIN) ? scanned : _vehicle.CurrentVehicle;
        var vin = info is not null && IsKnown(info.VIN) && info.VIN.Length >= 11 ? info.VIN : null;

        var trips = (await _db.GetFuelTripsAsync(vin)).Where(t => t.FinishedAt >= t.StartedAt && t.StartedAt.Year > 2000).ToList();
        var refuels = await _db.GetRefuelsAsync(vin);
        var scans = await _db.GetScanSessionsAsync(vin);
        var runs = await _db.GetAccelerationRunsAsync(vin);

        var fallbackPrice = FallbackPrice(refuels);
        var sb = new StringBuilder();

        // ---------- header ----------
        sb.AppendLine("" + T("მანქანის ანგარიში", "Vehicle report"));
        sb.AppendLine(now.ToString("dd.MM.yyyy HH:mm", Inv));
        sb.AppendLine();

        AppendVehicle(sb, info, vin);
        AppendEcu(sb, ecu, ecuError);

        if (trips.Count == 0 && refuels.Count == 0 && scans.Count == 0 && runs.Count == 0)
        {
            if (ecu is not null)
                return sb.ToString().TrimEnd();

            sb.AppendLine(T(
                "ჯერ შენახული მონაცემი არ არის. ჩართე „საწვავის ხარჯი“ მგზავრობისას, გააკეთე სკანი ან დაამატე შევსება — და ანგარიში შეივსება.",
                "There is no saved data yet. Turn on Fuel Consumption while driving, run a scan or add a refuel and the report will fill up."));
            return sb.ToString().TrimEnd();
        }

        progress?.Report(T("მართვის სტილის ანალიზი…", "Analysing driving style…"));
        var saved = LoadReports(scans);
        var driving = await AnalyzeRecordingsAsync(vin);

        AppendHighlights(sb, ecu, trips, scans, driving, now);
        AppendDriving(sb, trips, now, fallbackPrice);
        AppendDrivingStyle(sb, driving);
        AppendBodyHealth(sb, saved, driving);
        AppendFaultHistory(sb, saved);
        AppendEcuPassport(sb, ecu, saved);
        AppendHealth(sb, scans, now);
        AppendHabits(sb, trips);
        AppendPerformance(sb, runs);
        AppendRecords(sb, trips);
        await AppendRecordingsAndMileageAsync(sb, vin, now);
        AppendFuel(sb, trips, refuels, now, fallbackPrice);
        AppendInsights(sb, trips, refuels, scans, now, fallbackPrice);

        sb.AppendLine();
        sb.AppendLine(T(
            "ციფრები მხოლოდ იმას ითვლის, რაც აპმა თავად ჩაიწერა (ჩართული საწვავის ხარჯი/სკანები/შევსებები). მგზავრობა ≈ ერთი ჩაწერილი გასვლა.",
            "Figures only count what the app recorded itself (Fuel Consumption sessions, scans, refuels). A trip ≈ one recorded drive."));

        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ sections

    private void AppendVehicle(StringBuilder sb, VehicleInfo? info, string? vin)
    {
        sb.AppendLine("▌ " + T("მანქანა", "Vehicle"));

        if (info is null || (!IsKnown(info.Manufacturer) && !IsKnown(info.Model) && vin is null))
        {
            sb.AppendLine(T("• ადაპტერი ახლა არ არის დაკავშირებული — ანგარიში მოიცავს ყველა შენახულ მონაცემს",
                "• Adapter is not connected right now — report covers all saved data"));
            sb.AppendLine();
            return;
        }

        var name = string.Join(" ", new[] { info.Manufacturer, info.Model, info.Year }.Where(IsKnown));
        if (name.Length > 0) sb.AppendLine("• " + name);
        if (vin is not null) sb.AppendLine("• VIN: " + vin);
        if (IsKnown(info.FuelType)) sb.AppendLine("• " + T("საწვავი: ", "Fuel: ") + info.FuelType);
        if (IsKnown(info.Engine)) sb.AppendLine("• " + T("ძრავა: ", "Engine: ") + info.Engine);
        if (IsKnown(info.Protocol)) sb.AppendLine("• " + T("პროტოკოლი: ", "Protocol: ") + info.Protocol);
        sb.AppendLine();
    }

    /// <summary>ECU section: only what the ECU itself reports as a problem (fault codes, MIL, out-of-range live values).</summary>
    private void AppendEcu(StringBuilder sb, ObdScanReport? ecu, string? error)
    {
        sb.AppendLine("▌ " + T("ECU — მხოლოდ პრობლემები", "ECU — problems only"));

        if (ecu is null)
        {
            sb.AppendLine("• " + (error ?? T("ადაპტერი დაკავშირებული არ არის — ცოცხალი ECU სკანი არ შესრულებულა.",
                "Adapter is not connected — no live ECU scan was made.")));
            sb.AppendLine();
            return;
        }

        if (!ecu.EcuResponded)
        {
            sb.AppendLine("• " + T("ECU არ გამოეხმაურა. ჩართე ანთება და სცადე თავიდან.", "The ECU did not respond. Turn the ignition on and try again."));
            sb.AppendLine();
            return;
        }

        var problems = 0;

        void Codes(IEnumerable<DiagnosticCode> codes, string kind)
        {
            foreach (var code in codes)
            {
                problems++;
                var description = string.IsNullOrWhiteSpace(code.Description) ? string.Empty : " — " + code.Description;
                sb.AppendLine($"• {code.Code}{description} [{kind}]");
            }
        }

        Codes(ecu.StoredCodes, T("შენახული", "Stored"));
        Codes(ecu.PendingCodes, T("მოლოდინში", "Pending"));
        Codes(ecu.PermanentCodes, T("მუდმივი", "Permanent"));

        // Other control units (ABS, SRS, BCM, …) read by the module scan.
        foreach (var module in ecu.Modules.Where(m => m.Codes.Count > 0))
        {
            var moduleName = T(module.NameKa, module.NameEn);
            foreach (var code in module.Codes)
            {
                problems++;
                var title = T(code.TitleKa, code.TitleEn);
                var state = code.Active ? T("აქტიური", "Active") : code.Pending ? T("მოლოდინში", "Pending") : T("შენახული", "Stored");
                sb.AppendLine($"• {moduleName}: {code.DisplayCode}{(string.IsNullOrWhiteSpace(title) ? string.Empty : " — " + title)} [{state}]");
            }
        }

        if (ecu.Readiness?.MilOn == true)
        {
            problems++;
            sb.AppendLine("• " + T("Check Engine (MIL) ანთია", "Check Engine (MIL) is on"));
        }

        foreach (var sensor in ecu.Sensors.Where(x => x.IsProblem))
        {
            problems++;
            sb.AppendLine($"• {sensor.Name}: {sensor.Value} {sensor.Unit} — {T("დასაშვებ დიაპაზონს გარეთაა", "outside the normal range")}".TrimEnd());
        }

        if (problems == 0)
        {
            sb.AppendLine("• " + T(
                $"ECU-მ პრობლემა არ აჩვენა: DTC 0, MIL გამორთული, {ecu.Sensors.Count} live პარამეტრი ნორმაშია.",
                $"The ECU reports no problems: 0 DTCs, MIL off, {ecu.Sensors.Count} live values are normal."));
        }

        if (ecu.DtcServicesRead < ecu.DtcServicesExpected)
            sb.AppendLine("• " + T($"DTC სერვისები წაკითხულია {ecu.DtcServicesRead}/{ecu.DtcServicesExpected} — ზოგი ECU-დან მიუწვდომელია.",
                $"DTC services read {ecu.DtcServicesRead}/{ecu.DtcServicesExpected} — some are unavailable from this ECU."));

        sb.AppendLine("• " + (ecu.ModulesChecked > 0
            ? T($"შემოწმდა ძრავის ECU და {ecu.ModulesChecked} სხვა მოდული.",
                $"Checked: engine ECU and {ecu.ModulesChecked} other module(s).")
            : T("შემოწმდა სტანდარტული OBD-II ძრავის ECU (ABS/SRS/BCM არა).",
                "Checked: standard OBD-II engine ECU (not ABS/SRS/BCM).")));
        sb.AppendLine();
    }

    private void AppendDriving(StringBuilder sb, List<FuelTripRecord> trips, DateTime now, double fallbackPrice)
    {
        if (trips.Count == 0)
            return;

        var t7 = trips.Where(t => t.StartedAt >= now.AddDays(-7)).ToList();
        var t30 = trips.Where(t => t.StartedAt >= now.AddDays(-30)).ToList();
        var km = trips.Sum(t => t.DistanceKm);
        var moving = trips.Sum(t => t.MovingSeconds);
        var idle = trips.Sum(t => t.IdleSeconds);
        var first = trips.Min(t => t.StartedAt);
        var last = trips.Max(t => t.FinishedAt);

        sb.AppendLine("▌ " + T("გარბენი და გასვლები", "Driving"));
        sb.AppendLine($"• {T("ჩაწერილი მგზავრობები სულ", "Trips recorded (all time)")}: {trips.Count}");
        sb.AppendLine($"• {T("ბოლო 7 დღე", "Last 7 days")}: {t7.Count} · {Dist(t7.Sum(t => t.DistanceKm))}");
        sb.AppendLine($"• {T("ბოლო 30 დღე", "Last 30 days")}: {t30.Count} · {Dist(t30.Sum(t => t.DistanceKm))}");
        sb.AppendLine($"• {T("ჩაწერილი გარბენი სულ", "Distance recorded (all time)")}: {Dist(km)}");
        if (trips.Count > 0 && km > 0)
            sb.AppendLine($"• {T("საშუალო მგზავრობა", "Average trip")}: {Dist(km / trips.Count)}");
        sb.AppendLine($"• {T("ჩაწერის პერიოდი", "Recording period")}: {first:dd.MM.yyyy} – {last:dd.MM.yyyy}");
        sb.AppendLine($"• {T("ბოლო მგზავრობა", "Last trip")}: {last:dd.MM.yyyy HH:mm} ({Ago(now, last)})");

        if (moving > 60)
        {
            sb.AppendLine($"• {T("მოძრაობაში გატარებული დრო", "Time moving")}: {Dur(moving)}");
            sb.AppendLine($"• {T("საშუალო სიჩქარე", "Average speed")}: {_units.FormatSpeed(km / (moving / 3600d))}");
        }

        if (idle + moving > 60)
            sb.AppendLine($"• {T("უქმ სვლაზე", "Idling")}: {Dur(idle)} ({idle / (idle + moving) * 100:0}%)");

        var maxSpeed = trips.Max(t => t.MaximumSpeedKph);
        if (maxSpeed > 0)
            sb.AppendLine($"• {T("მაქსიმალური სიჩქარე", "Top speed")}: {_units.FormatSpeed(maxSpeed)}");

        var activeDays = trips.Select(t => t.StartedAt.Date).Distinct().Count();
        sb.AppendLine($"• {T("აქტიური დღეები", "Days driven")}: {activeDays}");
        sb.AppendLine();
    }

    private void AppendFuel(StringBuilder sb, List<FuelTripRecord> trips, List<RefuelRecord> refuels, DateTime now, double fallbackPrice)
    {
        var withFuel = trips.Where(t => t.FuelLiters > 0 && t.DistanceKm > 0.2).ToList();

        if (withFuel.Count == 0 && refuels.Count == 0)
            return;

        sb.AppendLine("▌ " + T("საწვავი და ხარჯი", "Fuel & cost"));

        if (withFuel.Count > 0)
        {
            var liters = withFuel.Sum(t => t.FuelLiters);
            var km = withFuel.Sum(t => t.DistanceKm);
            sb.AppendLine($"• {T("საშუალო ხარჯი", "Average consumption")}: {_units.FormatFuelEconomy(liters / km * 100d)}");
            sb.AppendLine($"• {T("გამოყენებული საწვავი სულ", "Fuel used (all time)")}: {Fuel(liters)}");

            foreach (var (days, label) in new[] { (7, T("ბოლო 7 დღე", "Last 7 days")), (30, T("ბოლო 30 დღე", "Last 30 days")) })
            {
                var part = withFuel.Where(t => t.StartedAt >= now.AddDays(-days)).ToList();
                if (part.Count == 0)
                {
                    sb.AppendLine($"• {label}: {T("მონაცემი არ არის", "no data")}");
                    continue;
                }

                var l = part.Sum(t => t.FuelLiters);
                var cost = TripCost(part, fallbackPrice, out var priced);
                var text = $"{Fuel(l)} · {_units.FormatFuelEconomy(l / part.Sum(t => t.DistanceKm) * 100d)}";
                if (priced) text += $" · ₾{cost:0.00}";
                sb.AppendLine($"• {label}: {text}");
            }

            var totalCost = TripCost(withFuel, fallbackPrice, out var anyPrice);
            if (anyPrice)
            {
                sb.AppendLine($"• {T("ხარჯი სულ (ჩაწერილ გასვლებზე)", "Cost of recorded trips")}: ₾{totalCost:0.00}");
                sb.AppendLine($"• {T("1 კმ-ის ფასი", "Cost per distance")}: ₾{totalCost / _units.Convert(km, "km").Value:0.000} / {_units.DistanceUnit}");
            }
        }

        if (refuels.Count > 0)
        {
            var spent = refuels.Sum(r => r.Liters * r.PricePerLiter);
            var liters = refuels.Sum(r => r.Liters);
            var lastRefuel = refuels.OrderByDescending(r => r.RefueledAt).First();

            sb.AppendLine($"• {T("შევსებები სულ", "Refuels")}: {refuels.Count} · {Fuel(liters)} · ₾{spent:0.00}");
            if (liters > 0 && spent > 0)
                sb.AppendLine($"• {T("საშუალო ფასი ლიტრზე", "Average price per liter")}: ₾{spent / liters:0.00}");

            var r30 = refuels.Where(r => r.RefueledAt >= now.AddDays(-30)).ToList();
            var r7 = refuels.Where(r => r.RefueledAt >= now.AddDays(-7)).ToList();
            sb.AppendLine($"• {T("შევსებაზე დახარჯული: 7 დღე", "Spent on fuel: 7 days")}: ₾{r7.Sum(r => r.Liters * r.PricePerLiter):0.00} · {T("30 დღე", "30 days")}: ₾{r30.Sum(r => r.Liters * r.PricePerLiter):0.00}");
            sb.AppendLine($"• {T("ბოლო შევსება", "Last refuel")}: {lastRefuel.RefueledAt:dd.MM.yyyy} · {Fuel(lastRefuel.Liters)}"
                          + (string.IsNullOrWhiteSpace(lastRefuel.Station) ? string.Empty : $" · {lastRefuel.Station}"));

            var odo = refuels.OrderByDescending(r => r.RefueledAt).FirstOrDefault(r => r.OdometerKm > 0);
            if (odo is not null)
                sb.AppendLine($"• {T("ბოლო ცნობილი ოდომეტრი (შევსებიდან)", "Last known odometer (from refuel)")}: {Dist(odo.OdometerKm, "0")}");

            var tank = TankToTank(refuels);
            if (tank is not null)
                sb.AppendLine($"• {T("ავზიდან ავზამდე ხარჯი", "Tank-to-tank consumption")}: {_units.FormatFuelEconomy(tank.Value)}");
        }

        sb.AppendLine();
    }

    private void AppendHabits(StringBuilder sb, List<FuelTripRecord> trips)
    {
        if (trips.Count < 3)
            return;

        sb.AppendLine("▌ " + T("ჩვევები", "Habits"));

        var byDay = trips.GroupBy(t => t.StartedAt.DayOfWeek).OrderByDescending(g => g.Sum(t => t.DistanceKm)).First();
        sb.AppendLine($"• {T("ყველაზე აქტიური დღე", "Busiest weekday")}: {DayName(byDay.Key)} ({byDay.Count()})");

        var buckets = new (string Name, int Count)[]
        {
            (T("ღამე (00–05)", "Night (00–05)"), trips.Count(t => t.StartedAt.Hour < 5)),
            (T("დილა (05–12)", "Morning (05–12)"), trips.Count(t => t.StartedAt.Hour is >= 5 and < 12)),
            (T("დღე (12–18)", "Afternoon (12–18)"), trips.Count(t => t.StartedAt.Hour is >= 12 and < 18)),
            (T("საღამო (18–24)", "Evening (18–24)"), trips.Count(t => t.StartedAt.Hour >= 18))
        };
        var top = buckets.OrderByDescending(b => b.Count).First();
        sb.AppendLine($"• {T("ყველაზე ხშირად დაიძვრები", "Most trips start")}: {top.Name} ({top.Count * 100 / trips.Count}%)");

        var cityShare = trips.Count(t => t.AverageSpeedKph > 0 && t.AverageSpeedKph <= 40);
        var hwyShare = trips.Count(t => t.AverageSpeedKph >= 70);
        if (cityShare + hwyShare > 0)
            sb.AppendLine($"• {T("ქალაქის / ტრასის მგზავრობები", "City / highway trips")}: {cityShare} / {hwyShare}");

        sb.AppendLine();
    }

    private void AppendHealth(StringBuilder sb, List<ScanSession> scans, DateTime now)
    {
        if (scans.Count == 0)
            return;

        var last = scans.OrderByDescending(s => s.StartedAt).First();
        var withFaults = scans.Count(s => s.StoredDtcCount + s.PendingDtcCount + s.PermanentDtcCount > 0 || s.MilOn);

        sb.AppendLine("▌ " + T("მდგომარეობა და სკანები", "Health & scans"));
        sb.AppendLine($"• {T("სკანები სულ", "Scans")}: {scans.Count} ({T("შეცდომებით", "with faults")}: {withFaults})");
        sb.AppendLine($"• {T("ბოლო სკანი", "Last scan")}: {last.StartedAt:dd.MM.yyyy HH:mm} ({Ago(now, last.StartedAt)})");
        sb.AppendLine($"• {T("ბოლო სკანზე", "In the last scan")}: Stored {last.StoredDtcCount} · Pending {last.PendingDtcCount} · Permanent {last.PermanentDtcCount} · MIL {(last.MilOn ? "ON" : "OFF")}");
        sb.AppendLine($"• {T("სკანები ბოლო 30 დღეში", "Scans in the last 30 days")}: {scans.Count(s => s.StartedAt >= now.AddDays(-30))}");
        sb.AppendLine();
    }

    private void AppendPerformance(StringBuilder sb, List<AccelerationRun> runs)
    {
        var valid = runs.Where(r => r.DurationSeconds > 0).ToList();
        if (valid.Count == 0)
            return;

        var best = valid.OrderBy(r => r.DurationSeconds).First();
        var latest = valid.OrderByDescending(r => r.FinishedAt).First();

        sb.AppendLine("▌ " + T("დინამიკა", "Performance") + " · " + _units.PerformanceTestLabel);
        sb.AppendLine($"• {T("ჩაწერილი გაჩქარებები", "Runs")}: {valid.Count}");
        sb.AppendLine($"• {T("საუკეთესო", "Best")}: {best.DurationSeconds:0.00} s ({best.FinishedAt:dd.MM.yyyy})");
        sb.AppendLine($"• {T("ბოლო", "Latest")}: {latest.DurationSeconds:0.00} s");
        sb.AppendLine($"• {T("საშუალო", "Average")}: {valid.Average(r => r.DurationSeconds):0.00} s");
        sb.AppendLine();
    }

    private void AppendRecords(StringBuilder sb, List<FuelTripRecord> trips)
    {
        if (trips.Count < 2)
            return;

        var longest = trips.OrderByDescending(t => t.DistanceKm).First();
        var economical = trips
            .Where(t => t.AverageFuelLPer100Km is > 0 && t.DistanceKm >= 3)
            .OrderBy(t => t.AverageFuelLPer100Km)
            .ToList();

        sb.AppendLine("▌ " + T("რეკორდები", "Records"));
        sb.AppendLine($"• {T("ყველაზე გრძელი მგზავრობა", "Longest trip")}: {Dist(longest.DistanceKm)} ({longest.StartedAt:dd.MM.yyyy})");

        if (economical.Count >= 2)
        {
            sb.AppendLine($"• {T("ყველაზე ეკონომიური", "Most economical")}: {_units.FormatFuelEconomy(economical.First().AverageFuelLPer100Km!.Value)} ({economical.First().StartedAt:dd.MM.yyyy})");
            sb.AppendLine($"• {T("ყველაზე ხარჯიანი", "Least economical")}: {_units.FormatFuelEconomy(economical.Last().AverageFuelLPer100Km!.Value)} ({economical.Last().StartedAt:dd.MM.yyyy})");
        }

        sb.AppendLine();
    }

    private void AppendInsights(
        StringBuilder sb,
        List<FuelTripRecord> trips,
        List<RefuelRecord> refuels,
        List<ScanSession> scans,
        DateTime now,
        double fallbackPrice)
    {
        var tips = new List<string>();

        var idle = trips.Sum(t => t.IdleSeconds);
        var moving = trips.Sum(t => t.MovingSeconds);
        if (idle + moving > 1800 && idle / (idle + moving) > 0.25)
            tips.Add(T($"უქმ სვლაზე დრო {idle / (idle + moving) * 100:0}%-ია — ეს ზედმეტ საწვავს წვავს.",
                $"Idling is {idle / (idle + moving) * 100:0}% of the time — that burns extra fuel."));

        var recent = trips.Where(t => t.StartedAt >= now.AddDays(-30) && t.FuelLiters > 0 && t.DistanceKm > 0.2).ToList();
        var before = trips.Where(t => t.StartedAt < now.AddDays(-30) && t.StartedAt >= now.AddDays(-60) && t.FuelLiters > 0 && t.DistanceKm > 0.2).ToList();
        if (recent.Sum(t => t.DistanceKm) > 20 && before.Sum(t => t.DistanceKm) > 20)
        {
            var a = recent.Sum(t => t.FuelLiters) / recent.Sum(t => t.DistanceKm) * 100d;
            var b = before.Sum(t => t.FuelLiters) / before.Sum(t => t.DistanceKm) * 100d;
            var change = (a - b) / b * 100d;
            if (Math.Abs(change) >= 10)
                tips.Add(change > 0
                    ? T($"ხარჯი წინა 30 დღესთან შედარებით {change:0}%-ით გაიზარდა — შეამოწმე საბურავების წნევა და ფილტრები.",
                        $"Consumption is up {change:0}% versus the previous 30 days — check tyre pressure and filters.")
                    : T($"ხარჯი წინა 30 დღესთან შედარებით {-change:0}%-ით შემცირდა — კარგი ტენდენციაა.",
                        $"Consumption is down {-change:0}% versus the previous 30 days — a good trend."));
        }

        if (scans.Count > 0)
        {
            var last = scans.OrderByDescending(s => s.StartedAt).First();
            if (last.MilOn || last.StoredDtcCount + last.PermanentDtcCount > 0)
                tips.Add(T("ბოლო სკანზე შეცდომის კოდებია — გახსენი „შეცდომების სკანირება“ დეტალებისთვის.",
                    "The last scan reported fault codes — open Scan for Errors for details."));
            if ((now - last.StartedAt).TotalDays > 30)
                tips.Add(T("30 დღეზე მეტია სკანი არ გაგიკეთებია.", "More than 30 days since the last scan."));
        }
        else
        {
            tips.Add(T("ჯერ სკანი არ გაგიკეთებია — გააკეთე, რომ მდგომარეობა ანგარიშში გამოჩნდეს.",
                "No scan yet — run one so the vehicle health appears in the report."));
        }

        if (trips.Count > 0 && fallbackPrice <= 0)
            tips.Add(T("საწვავის ფასი არ არის შეყვანილი, ამიტომ ხარჯი ლარებში არ ითვლება.",
                "No fuel price is set, so cost in money can't be calculated."));

        if (tips.Count == 0)
            return;

        sb.AppendLine("▌ " + T("რჩევები", "Insights"));
        foreach (var tip in tips)
            sb.AppendLine("• " + tip);
        sb.AppendLine();
    }

    // ------------------------------------------------------------------ helpers

    private static bool IsKnown(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
        !value.Equals("Pending", StringComparison.OrdinalIgnoreCase);

    private string Dist(double km, string format = "0.0") => _units.FormatDistance(km, format);

    private string Fuel(double liters) => _units.Format(liters, "L", "0.0");

    private static string Dur(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours} h {span.Minutes} min"
            : $"{span.Minutes} min";
    }

    private string Ago(DateTime now, DateTime then)
    {
        var days = (int)Math.Floor((now - then).TotalDays);
        if (days <= 0) return T("დღეს", "today");
        if (days == 1) return T("გუშინ", "yesterday");
        return T($"{days} დღის წინ", $"{days} days ago");
    }

    private string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => T("ორშაბათი", "Monday"),
        DayOfWeek.Tuesday => T("სამშაბათი", "Tuesday"),
        DayOfWeek.Wednesday => T("ოთხშაბათი", "Wednesday"),
        DayOfWeek.Thursday => T("ხუთშაბათი", "Thursday"),
        DayOfWeek.Friday => T("პარასკევი", "Friday"),
        DayOfWeek.Saturday => T("შაბათი", "Saturday"),
        _ => T("კვირა", "Sunday")
    };

    /// <summary>Price used for trips that have no stored price: weighted refuel price, else the price typed on the fuel page.</summary>
    private static double FallbackPrice(List<RefuelRecord> refuels)
    {
        var liters = refuels.Where(r => r.PricePerLiter > 0).Sum(r => r.Liters);
        if (liters > 0)
            return refuels.Where(r => r.PricePerLiter > 0).Sum(r => r.Liters * r.PricePerLiter) / liters;

        var text = Preferences.Default.Get("fuel_price_per_liter", string.Empty).Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, Inv, out var price) && price > 0 ? price : 0;
    }

    private static double TripCost(List<FuelTripRecord> trips, double fallbackPrice, out bool anyPrice)
    {
        anyPrice = false;
        var total = 0d;
        foreach (var trip in trips)
        {
            var price = trip.FuelPricePerLiter is > 0 ? trip.FuelPricePerLiter.Value : fallbackPrice;
            if (price <= 0)
                continue;

            anyPrice = true;
            total += trip.FuelLiters * price;
        }

        return total;
    }

    /// <summary>Average L/100 km between consecutive full-tank refuels (partial fills in between are added up).</summary>
    private static double? TankToTank(List<RefuelRecord> refuels)
    {
        double? lastFullOdo = null;
        var liters = 0d;
        var litersTotal = 0d;
        var kmTotal = 0d;

        foreach (var record in refuels.OrderBy(r => r.RefueledAt))
        {
            if (lastFullOdo is not null)
                liters += record.Liters;

            if (!record.IsFullTank || record.OdometerKm <= 0)
                continue;

            if (lastFullOdo is not null && record.OdometerKm > lastFullOdo.Value && liters > 0)
            {
                litersTotal += liters;
                kmTotal += record.OdometerKm - lastFullOdo.Value;
            }

            lastFullOdo = record.OdometerKm;
            liters = 0;
        }

        return kmTotal > 20 ? litersTotal / kmTotal * 100d : null;
    }
}
