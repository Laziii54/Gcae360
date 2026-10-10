using CarScanner.AI;
using CarScanner.Models;

namespace CarScanner.Services;

public sealed class RepairCostService
{
    private readonly AiDiagnosticService _ai;

    public RepairCostService(AiDiagnosticService ai) => _ai = ai;

    public async Task<RepairCostResult> EstimateAsync(
        VehicleInfo vehicle,
        IReadOnlyList<ErrorCode> codes,
        CancellationToken token = default)
    {
        if (codes.Count == 0)
            return new(Array.Empty<RepairCostEstimate>(), false, string.Empty);

        try
        {
            var ai = await _ai.EstimateRepairCostsAsync(vehicle, codes, token);
            var byCode = ai.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
            var complete = codes.Select(code =>
                byCode.TryGetValue(Normalize(code.Code), out var estimate)
                    ? estimate
                    : BuildFallback(code)).ToArray();
            return new(complete, true, "AI შეფასება · ავტომობილის მოდელი · ქართული ბაზარი");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(
                codes.Select(BuildFallback).ToArray(),
                false,
                "საწყისი სავარაუდო დიაპაზონები · AI დროებით მიუწვდომელია");
        }
    }

    private static RepairCostEstimate BuildFallback(ErrorCode code)
    {
        var normalized = Normalize(code.Code);
        if (Known.TryGetValue(normalized, out var known))
            return known.WithError(code);

        var urgency = InferUrgency(code);
        if (TryMarketRange(normalized, out var market))
        {
            return new RepairCostEstimate
            {
                Code = normalized,
                ProblemName = code.DisplayTitle,
                PartMinGel = market.PartMin,
                PartMaxGel = market.PartMax,
                LaborMinGel = market.LaborMin,
                LaborMaxGel = market.LaborMax,
                Urgency = urgency,
                Reason = "ფასი დამოკიდებულია ზუსტ მიზეზზე, ნაწილის ბრენდსა და ავტომობილის მოდიფიკაციაზე.",
                Source = "ხშირი კოდის საწყისი დიაპაზონი"
            };
        }

        var (partMin, partMax, laborMin, laborMax) = normalized.FirstOrDefault() switch
        {
            'B' => (80, 1200, 50, 350),
            'C' => (100, 1800, 80, 500),
            'U' => (50, 900, 60, 400),
            _ => (60, 1500, 50, 450)
        };
        return new RepairCostEstimate
        {
            Code = normalized,
            ProblemName = code.DisplayTitle,
            PartMinGel = partMin,
            PartMaxGel = partMax,
            LaborMinGel = laborMin,
            LaborMaxGel = laborMax,
            Urgency = urgency,
            Reason = "ზუსტი მიზეზი და შესაცვლელი ნაწილი დიაგნოსტიკით უნდა დადასტურდეს.",
            Source = "საწყისი სავარაუდო დიაპაზონი"
        };
    }

    // Any code the scanner can show maps to the parts that normally cause it, priced from real myparts.ge listings
    // (see MarketPartPrices). Same-length codes compare correctly as strings (P0100..P0104). Labour is a garage range.
    private static readonly (string From, string To, string[] Parts, int LaborMin, int LaborMax)[] PartsByCode =
    {
        ("P0010", "P0025", new[] { "vvt_solenoid" }, 80, 350),
        ("P0100", "P0104", new[] { "maf" }, 30, 100),
        ("P0105", "P0109", new[] { "map" }, 30, 100),
        ("P0110", "P0119", new[] { "temp_sensor" }, 30, 100),
        ("P0120", "P0124", new[] { "throttle_body" }, 50, 200),
        ("P0125", "P0129", new[] { "thermostat", "temp_sensor" }, 60, 180),
        ("P0130", "P0167", new[] { "o2_sensor" }, 40, 120),
        ("P0190", "P0194", new[] { "fuel_pump" }, 70, 250),
        ("P0200", "P0212", new[] { "injector" }, 60, 250),
        ("P0220", "P0229", new[] { "throttle_body" }, 50, 200),
        ("P0230", "P0233", new[] { "fuel_pump" }, 70, 250),
        ("P0235", "P0249", new[] { "turbo" }, 150, 450),
        ("P0299", "P0299", new[] { "turbo" }, 150, 450),
        ("P0300", "P0312", new[] { "spark_plugs", "ignition_coil" }, 40, 200),
        ("P0325", "P0334", new[] { "knock_sensor" }, 40, 150),
        ("P0335", "P0339", new[] { "crank_sensor" }, 40, 150),
        ("P0340", "P0349", new[] { "cam_sensor" }, 40, 150),
        ("P0351", "P0362", new[] { "ignition_coil" }, 30, 120),
        ("P0380", "P0389", new[] { "glow_plug" }, 60, 250),
        ("P0400", "P0409", new[] { "egr_valve" }, 70, 250),
        ("P0420", "P0439", new[] { "catalyst" }, 50, 180),
        ("P0440", "P0459", new[] { "fuel_cap", "purge_valve" }, 40, 180),
        ("P0500", "P0503", new[] { "abs_sensor" }, 40, 120),
        ("P0505", "P0509", new[] { "throttle_body" }, 50, 200),
        ("P0560", "P0579", new[] { "battery", "alternator" }, 30, 150),
        ("P0600", "P0699", new[] { "ecu" }, 100, 400),
        ("C0030", "C0059", new[] { "abs_sensor", "wheel_bearing" }, 60, 250),
        ("C0750", "C0779", new[] { "tpms_sensor" }, 20, 80),
        ("B0001", "B0099", new[] { "airbag" }, 100, 400),
        // Transmission (P07xx): sensors, solenoids / fluid, control unit, internal failure.
        ("P0700", "P0704", new[] { "tcm" }, 100, 400),
        ("P0705", "P0729", new[] { "trans_sensor", "abs_sensor" }, 50, 200),
        ("P0730", "P0739", new[] { "trans_fluid", "trans_filter", "trans_solenoid", "trans_unit" }, 150, 800),
        ("P0740", "P0749", new[] { "trans_fluid", "trans_filter", "trans_solenoid" }, 150, 500),
        ("P0750", "P0799", new[] { "trans_fluid", "trans_filter", "trans_solenoid" }, 120, 450),
        // Network (U0xxx): usually wiring / power / ground, otherwise the module that stopped answering.
        ("U0100", "U0100", new[] { "none", "ecu" }, 100, 400),
        ("U0101", "U0101", new[] { "none", "tcm" }, 100, 400),
        ("U0121", "U0121", new[] { "none", "abs_unit" }, 100, 400),
        ("U0140", "U0140", new[] { "none", "body_module" }, 100, 350),
        ("U0155", "U0155", new[] { "none", "cluster" }, 100, 350),
        ("U0001", "U0099", new[] { "none", "ecu" }, 80, 300),
        ("U0100", "U0199", new[] { "none", "body_module", "ecu" }, 100, 400),
        ("U0200", "U0299", new[] { "none", "body_module" }, 100, 350),
        ("U0300", "U0499", new[] { "none", "ecu" }, 100, 400)
    };

    private static bool TryMarketRange(string code, out (int PartMin, int PartMax, int LaborMin, int LaborMax) range)
    {
        range = default;
        if (code.Length != 5)
            return false;

        foreach (var (from, to, parts, laborMin, laborMax) in PartsByCode)
        {
            if (code[0] != from[0] ||
                string.CompareOrdinal(code, from) < 0 ||
                string.CompareOrdinal(code, to) > 0)
            {
                continue;
            }

            var (min, max) = MarketPartPrices.Range(parts);
            range = (min, max, laborMin, laborMax);
            return true;
        }

        return false;
    }

    private static RepairUrgency InferUrgency(ErrorCode code)
    {
        var text = $"{code.DisplayTitle} {code.DisplayDescription} {code.DisplaySymptoms}".ToLowerInvariant();
        if (new[] { "brake", "airbag", "steering", "overheat", "oil pressure", "მუხრუჭ", "აირბაგ", "საჭ", "გადახურ", "ზეთის წნევა" }.Any(text.Contains))
            return RepairUrgency.StopNow;
        return code.Severity switch
        {
            "სერიოზული" or "მაღალი" or "High" or "Critical" => RepairUrgency.RepairSoon,
            _ => RepairUrgency.CanWait
        };
    }

    private static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    // Part prices come from real myparts.ge listings (see MarketPartPrices); labour is a typical garage range
    // because myparts.ge publishes no labour prices.
    private static KnownEstimate K(string name, (int Min, int Max) parts, int laborMin, int laborMax, RepairUrgency urgency) =>
        new(name, parts.Min, parts.Max, laborMin, laborMax, urgency);

    private static readonly IReadOnlyDictionary<string, KnownEstimate> Known = BuildKnown();

    private static IReadOnlyDictionary<string, KnownEstimate> BuildKnown()
    {
        var catalyst = MarketPartPrices.Range("catalyst");
        var misfire = MarketPartPrices.Range("spark_plugs", "ignition_coil");
        var o2 = MarketPartPrices.Range("o2_sensor");
        var coolantTemp = MarketPartPrices.Range("temp_sensor");
        var voltage = MarketPartPrices.Range("battery", "alternator");
        var throttle = MarketPartPrices.Range("throttle_body");
        var fuelPressure = MarketPartPrices.Range("fuel_pump");
        var speedSensor = MarketPartPrices.Range("abs_sensor");

        var known = new Dictionary<string, KnownEstimate>(StringComparer.OrdinalIgnoreCase)
        {
            ["P0420"] = K("კატალიზატორის ეფექტურობა", catalyst, 50, 150, RepairUrgency.RepairSoon),
            ["P0430"] = K("კატალიზატორის ეფექტურობა (Bank 2)", catalyst, 60, 180, RepairUrgency.RepairSoon),
            ["P0300"] = K("ძრავის არათანაბარი წვა", misfire, 50, 250, RepairUrgency.RepairSoon),
            ["P0171"] = K("ღარიბი საწვავის ნარევი", (30, 400), 50, 220, RepairUrgency.RepairSoon),
            ["P0128"] = K("თერმოსტატი / დაბალი სამუშაო ტემპერატურა", (60, 100), 70, 180, RepairUrgency.CanWait),
            ["P0455"] = K("EVAP სისტემის დიდი გაჟონვა", (20, 450), 50, 180, RepairUrgency.CanWait),
            ["P0442"] = K("EVAP სისტემის მცირე გაჟონვა", (20, 350), 50, 180, RepairUrgency.CanWait),
            ["P0011"] = K("შემშვები კამშაფტის ფაზა", (90, 600), 80, 350, RepairUrgency.RepairSoon),
            ["P0014"] = K("გამშვები კამშაფტის ფაზა", (90, 600), 80, 350, RepairUrgency.RepairSoon),
            ["P0299"] = K("ტურბოს წნევა ნაკლებია", (450, 900), 150, 450, RepairUrgency.RepairSoon),
            ["P0234"] = K("ტურბოს წნევა მეტია", (450, 900), 150, 450, RepairUrgency.RepairSoon),
            ["P0500"] = K("სიჩქარის სენსორი", speedSensor, 40, 120, RepairUrgency.CanWait),
            ["P0562"] = K("ბორტის ძაბვა დაბალია", voltage, 30, 150, RepairUrgency.RepairSoon),
            ["P0087"] = K("საწვავის წნევა დაბალია", fuelPressure, 70, 250, RepairUrgency.RepairSoon),
            ["P0190"] = K("საწვავის წნევის სენსორი", fuelPressure, 70, 250, RepairUrgency.RepairSoon),
            ["P0505"] = K("უქმი სვლის მართვა", throttle, 50, 200, RepairUrgency.CanWait),
            ["P0121"] = K("დროსელის / პედლის სენსორი", throttle, 50, 200, RepairUrgency.RepairSoon),
            ["P0122"] = K("დროსელის / პედლის სენსორი (დაბალი სიგნალი)", throttle, 50, 200, RepairUrgency.RepairSoon),
            ["P0123"] = K("დროსელის / პედლის სენსორი (მაღალი სიგნალი)", throttle, 50, 200, RepairUrgency.RepairSoon),
            ["P0117"] = K("გამაგრილებლის ტემპერატურის სენსორი", coolantTemp, 30, 100, RepairUrgency.CanWait),
            ["P0118"] = K("გამაგრილებლის ტემპერატურის სენსორი", coolantTemp, 30, 100, RepairUrgency.CanWait),
            ["P0112"] = K("შემშვები ჰაერის ტემპერატურის სენსორი", coolantTemp, 30, 100, RepairUrgency.CanWait),
            ["P0113"] = K("შემშვები ჰაერის ტემპერატურის სენსორი", coolantTemp, 30, 100, RepairUrgency.CanWait),
        };

        // Cylinder-specific misfires and the oxygen sensor family share one estimate each.
        for (var cylinder = 1; cylinder <= 8; cylinder++)
            known[$"P030{cylinder}"] = K($"ცილინდრი {cylinder}-ის წვის გამოტოვება", misfire, 40, 150, RepairUrgency.RepairSoon);

        foreach (var code in new[] { "P0130", "P0131", "P0132", "P0133", "P0134", "P0135", "P0136", "P0137", "P0138", "P0139", "P0140", "P0141" })
            known[code] = K("ჟანგბადის სენსორი (ლამბდა)", o2, 40, 120, RepairUrgency.CanWait);

        return known;
    }

    private sealed record KnownEstimate(
        string Name,
        int PartMin,
        int PartMax,
        int LaborMin,
        int LaborMax,
        RepairUrgency Urgency)
    {
        public RepairCostEstimate WithError(ErrorCode code) => new()
        {
            Code = code.Code,
            ProblemName = Name,
            PartMinGel = PartMin,
            PartMaxGel = PartMax,
            LaborMinGel = LaborMin,
            LaborMaxGel = LaborMax,
            Urgency = Urgency,
            Reason = "ფასი დამოკიდებულია ზუსტ მიზეზზე, ნაწილის ბრენდსა და ავტომობილის მოდიფიკაციაზე.",
            Source = "ხშირი კოდის საწყისი დიაპაზონი"
        };
    }
}

public sealed record RepairCostResult(
    IReadOnlyList<RepairCostEstimate> Estimates,
    bool UsedAi,
    string SourceMessage);
