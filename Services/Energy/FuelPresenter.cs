using System.Globalization;

namespace CarScanner.Services.Energy;

/// <summary>What the headline number of the Fuel page shows.</summary>
public sealed record InstantDisplay(
    string Value,
    string Unit,
    string Note,
    FuelDataQuality Quality,
    bool IsAvailable);

/// <summary>
/// Turns engine results into the words and numbers on the page. The rules are fixed here, in one tested place:
///  • an estimate always carries "≈" and says so; a direct reading never does;
///  • where a consumption per distance is not defined (standing, engine off, no speed) the page says why instead of showing ∞ or 0;
///  • nothing is shown for a reading that is missing, old or implausible.
/// </summary>
public static class FuelPresenter
{
    public static InstantDisplay Instant(InstantFuel instant, FuelEconomyUnit unit, bool english)
    {
        var estimate = instant.Quality == FuelDataQuality.Estimated;
        var mark = estimate ? "≈ " : string.Empty;

        switch (instant.State)
        {
            case InstantFuelState.Driving when instant.LitersPer100Km is { } per100 && FuelUnits.FromLPer100Km(per100, unit) is { } shown:
                return new InstantDisplay(mark + shown.ToString("0.0", CultureInfo.InvariantCulture), FuelUnits.Label(unit),
                    QualityLabel(instant.Quality, english), instant.Quality, true);

            case InstantFuelState.FuelCut:
                // Zero fuel per distance: L/100 km can say "0.0"; for the distance-per-volume units the result would be infinite.
                return unit == FuelEconomyUnit.LitersPer100Km
                    ? new InstantDisplay(mark + "0.0", FuelUnits.Label(unit), english ? "Fuel cut — coasting" + (estimate ? " (inferred)" : string.Empty) : "საწვავი ითიშება — ინერცია" + (estimate ? " (დასკვნით)" : string.Empty), instant.Quality, true)
                    : new InstantDisplay("—", FuelUnits.Label(unit), english ? "Fuel cut — coasting, no fuel used" + (estimate ? " (inferred)" : string.Empty) : "საწვავი ითიშება — ინერცია, საწვავი არ იხარჯება" + (estimate ? " (დასკვნით)" : string.Empty), instant.Quality, true);

            case InstantFuelState.Idle when instant.LitersPerHour is { } idle:
                return new InstantDisplay(mark + FlowNumber(idle, unit), FlowUnit(unit),
                    english ? "Idle or very slow — consumption per distance is not defined, showing the flow" : "უძრავად ან ძალიან ნელა — ხარჯი მანძილზე არ განისაზღვრება, ნაჩვენებია დინება",
                    instant.Quality, true);

            case InstantFuelState.FlowOnly when instant.LitersPerHour is { } flow:
                return new InstantDisplay(mark + FlowNumber(flow, unit), FlowUnit(unit),
                    english ? "No current speed reading — showing the flow only" : "სიჩქარე ახლა არ მოდის — ნაჩვენებია მხოლოდ დინება",
                    instant.Quality, true);

            case InstantFuelState.EngineOff:
                return new InstantDisplay("—", FuelUnits.Label(unit), english ? "Engine off — no fuel is used" : "ძრავი გამორთულია — საწვავი არ იხარჯება", instant.Quality, true);

            default:
                return new InstantDisplay("—", FuelUnits.Label(unit), IssueText(instant.Issue, english), FuelDataQuality.None, false);
        }
    }

    private static string FlowNumber(double litersPerHour, FuelEconomyUnit unit) =>
        FuelUnits.Volume(litersPerHour, unit).Value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FlowUnit(FuelEconomyUnit unit) => FuelUnits.Volume(1, unit).Unit + "/h";

    public static string QualityLabel(FuelDataQuality quality, bool english) => quality switch
    {
        FuelDataQuality.Direct => english ? "Direct — fuel rate reported by the ECU" : "პირდაპირი — ECU-ს მიერ გადმოცემული ხარჯი",
        FuelDataQuality.Estimated => english ? "Estimate — calculated from air flow" : "შეფასება — ჰაერის ხარჯიდან გამოთვლილი",
        _ => english ? "No fuel data" : "საწვავის მონაცემი არ არის"
    };

    public static string TripQualityLabel(TripFuelQuality quality, double coverage, bool english)
    {
        var text = quality switch
        {
            TripFuelQuality.Direct => english ? "Direct — fuel rate reported by the ECU" : "პირდაპირი — ECU-ს მიერ გადმოცემული ხარჯი",
            TripFuelQuality.Estimated => english ? "Estimate — calculated from air flow" : "შეფასება — ჰაერის ხარჯიდან გამოთვლილი",
            TripFuelQuality.Mixed => english ? "Mixed — part reported by the ECU, part estimated" : "შერეული — ნაწილი ECU-დან, ნაწილი შეფასებული",
            _ => english ? "No fuel data" : "საწვავის მონაცემი არ არის"
        };

        return quality != TripFuelQuality.None && coverage < FuelTripEngine.MinimumCoverage
            ? text + (english ? $" · only {coverage * 100:0}% of the running time had a reading" : $" · გაშვებული დროის მხოლოდ {coverage * 100:0}%-ზე იყო მონაცემი")
            : text;
    }

    /// <summary>Trip average as text: "≈" for an estimate, a reliability warning for partial data, "—" without enough distance or fuel.</summary>
    public static string Average(FuelTripSnapshot trip, FuelEconomyUnit unit)
    {
        if (trip.AverageLPer100Km is not { } average || FuelUnits.FromLPer100Km(average, unit) is not { } shown)
            return "—";

        var mark = trip.Quality is TripFuelQuality.Estimated or TripFuelQuality.Mixed || !trip.HasReliableAverage ? "≈ " : string.Empty;
        return $"{mark}{shown.ToString("0.0", CultureInfo.InvariantCulture)} {FuelUnits.Label(unit)}";
    }

    public static string FuelUsed(FuelTripSnapshot trip, FuelEconomyUnit unit) =>
        trip.FuelLiters > 0
            ? FuelUnits.FormatVolume(trip.FuelLiters, unit, trip.Quality is TripFuelQuality.Estimated or TripFuelQuality.Mixed)
            : "—";

    public static string IssueText(FuelRateIssue issue, bool english) => issue switch
    {
        FuelRateIssue.Stale => english ? "Readings stopped arriving — nothing is shown instead of an old value" : "მონაცემი შეწყდა — ძველი მნიშვნელობის ნაცვლად არაფერი ჩანს",
        FuelRateIssue.Implausible => english ? "An implausible reading was ignored" : "არარეალური მნიშვნელობა უგულებელყოფილია",
        FuelRateIssue.FuelTypeUnknown => english ? "Fuel type unknown — choose it below; an air-flow estimate needs it" : "საწვავის ტიპი უცნობია — აირჩიე ქვემოთ; ჰაერის ხარჯით გამოთვლას ეს სჭირდება",
        FuelRateIssue.FuelTypeUnsupported => english ? "This fuel cannot be calculated in litres" : "ამ საწვავის ხარჯი ლიტრებში ვერ გამოითვლება",
        FuelRateIssue.DieselNeedsLambda => english ? "Diesel: air flow alone cannot give fuel use — a wide-band λ reading is needed" : "დიზელი: მხოლოდ ჰაერის ხარჯი საწვავს ვერ გაჩვენებს — საჭიროა λ სენსორის მონაცემი",
        FuelRateIssue.ElectricVehicle => english ? "Electric vehicle — no liquid fuel" : "ელექტრომობილი — თხევადი საწვავი არ არის",
        FuelRateIssue.EngineOff => english ? "Engine off" : "ძრავი გამორთულია",
        _ => english ? "This car has not reported a fuel rate or air flow yet" : "ამ მანქანამ საწვავის ხარჯი ან ჰაერის ხარჯი ჯერ არ გადმოცა"
    };

    // ---- what the car supports -------------------------------------------------------------

    public static string MetricName(EnergyMetric metric, bool english) => metric switch
    {
        EnergyMetric.InstantFuel => english ? "Instant fuel use" : "მომენტალური ხარჯი",
        EnergyMetric.AverageFuel => english ? "Trip average" : "მოგზაურობის საშუალო",
        EnergyMetric.TripFuel => english ? "Fuel used" : "დახარჯული საწვავი",
        EnergyMetric.TripDistance => english ? "Distance" : "მანძილი",
        EnergyMetric.FuelLevel => english ? "Fuel level" : "საწვავის დონე",
        EnergyMetric.EngineState => english ? "Engine on / off" : "ძრავი ჩართულია / გამორთულია",
        EnergyMetric.ElectricDriveShare => english ? "Driven with the engine off" : "ძრავის გარეშე ნავალი",
        EnergyMetric.StateOfCharge => english ? "Battery %" : "ბატარეის %",
        EnergyMetric.ElectricPower => english ? "Battery power" : "ბატარეის სიმძლავრე",
        EnergyMetric.ElectricEnergy => english ? "Electric energy used" : "დახარჯული ელექტროენერგია",
        _ => english ? "Regenerated energy" : "რეკუპერაციით დაბრუნებული"
    };

    public static string SupportLabel(EnergySupport support, bool english) => support switch
    {
        EnergySupport.DirectlySupported => english ? "reported by the car" : "მანქანა თავად გადმოცემს",
        EnergySupport.CalculatedFromAvailableData => english ? "calculated from reported data" : "გამოთვლილია გადმოცემული მონაცემიდან",
        EnergySupport.Estimated => english ? "estimate" : "შეფასება",
        EnergySupport.PartiallySupported => english ? "partly available" : "ნაწილობრივ",
        EnergySupport.InsufficientData => english ? "not enough data" : "მონაცემი არ ჯერა",
        _ => english ? "not available" : "მიუწვდომელია"
    };

    public static string ReasonText(EnergyReason reason, bool english) => reason switch
    {
        EnergyReason.NotApplicableToThisPowertrain => english ? "does not apply to this powertrain" : "ამ ძრავის ტიპს არ ეხება",
        EnergyReason.ListNotRead => english ? "the car's PID list has not been read yet" : "მანქანის PID სია ჯერ არ წაკითხულა",
        EnergyReason.NoFuelRatePid => english ? "no fuel-rate or air-flow PID" : "საწვავის ან ჰაერის ხარჯის PID არ არის",
        EnergyReason.FuelRatePid => "PID 01 5E",
        EnergyReason.FuelMassRatePid => english ? "PID 01 9D (g/s) ÷ density of the selected fuel" : "PID 01 9D (გ/წმ) ÷ არჩეული საწვავის სიმკვრივე",
        EnergyReason.AirFlowEstimate => english ? "air flow ÷ air-fuel ratio, assumed density" : "ჰაერის ხარჯი ÷ ჰაერ-საწვავის თანაფარდობა, დაშვებული სიმკვრივე",
        EnergyReason.FuelTypeUnknown => english ? "fuel type unknown" : "საწვავის ტიპი უცნობია",
        EnergyReason.DieselNeedsLambda => english ? "diesel needs a λ reading" : "დიზელს λ მონაცემი სჭირდება",
        EnergyReason.FuelTypeUnsupported => english ? "not calculated in litres" : "ლიტრებში არ გამოითვლება",
        EnergyReason.SpeedMissing => english ? "no speed PID" : "სიჩქარის PID არ არის",
        EnergyReason.RpmMissing => english ? "no rpm PID" : "ბრუნვების PID არ არის",
        EnergyReason.NoBatterySource => english ? "no battery data found" : "ბატარეის მონაცემი ვერ მოიძებნა",
        EnergyReason.AdapterCouldNotRead => english ? "the adapter could not read the battery module" : "ადაპტერმა ბატარეის მოდული ვერ წაიკითხა",
        EnergyReason.BatteryCurrentSource => english ? "voltage × current × time" : "ძაბვა × დენი × დრო",
        EnergyReason.SocWithoutCapacity => english ? "needs the battery capacity" : "ბატარეის ტევადობა სჭირდება",
        EnergyReason.SocWithCapacity => english ? "battery % × capacity" : "ბატარეის % × ტევადობა",
        EnergyReason.DerivedFromRpm => english ? "derived from rpm and speed" : "გამოთვლილია ბრუნვებიდან და სიჩქარიდან",
        EnergyReason.NoFuelLevelPid => english ? "no fuel-level PID" : "საწვავის დონის PID არ არის",
        _ => string.Empty
    };

    /// <summary>Icon kind (Controls/IconCatalog): "check" reported or calculated, "approx" estimate, "circle" not enough data yet, "close" not available.</summary>
    public static string Mark(EnergySupport support) => support switch
    {
        EnergySupport.DirectlySupported or EnergySupport.CalculatedFromAvailableData => "check",
        EnergySupport.Estimated or EnergySupport.PartiallySupported => "approx",
        EnergySupport.InsufficientData => "circle",
        _ => "close"
    };

    // ---- what is saved with a trip --------------------------------------------------------

    /// <summary>The text stored with a saved trip. An estimate is never described as a measurement.</summary>
    public static string TripSource(TripFuelQuality fuel, FuelKind kind, EnergyQuality energy, bool interrupted)
    {
        var parts = new List<string>();
        switch (fuel)
        {
            case TripFuelQuality.Direct: parts.Add("Fuel: reported by the ECU (PID 01 5E)"); break;
            case TripFuelQuality.Estimated: parts.Add($"Fuel: estimate from air flow (PID 01 10), fuel type {kind}"); break;
            case TripFuelQuality.Mixed: parts.Add($"Fuel: partly reported by the ECU, partly estimated from air flow ({kind})"); break;
        }

        switch (energy)
        {
            case EnergyQuality.Measured: parts.Add("Electric: battery voltage × current, integrated"); break;
            case EnergyQuality.Estimated: parts.Add("Electric: estimate from battery % × capacity"); break;
        }

        if (parts.Count == 0)
            parts.Add("No fuel or energy data");
        if (interrupted)
            parts.Add("recovered after the app stopped (the end of the trip is approximate)");
        return string.Join(" · ", parts);
    }
}
