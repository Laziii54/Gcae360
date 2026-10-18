using System.Text;

namespace CarScanner.Services;

/// <summary>
/// Part prices taken from real listings on myparts.ge (Georgian market), recorded on 2026-10-09.
///
/// How the numbers were made: for every part the public search of myparts.ge was read (first two result pages,
/// at most 30 listings) and the asking prices in ₾ were split into "new" (ახალი) and "used" (მეორადი). Low = 25th
/// percentile, Typical = median, High = 90th percentile (75th for used parts), so a single absurd listing does not
/// stretch a range. They are ASKING prices of sellers, not what people finally pay, and they cover the part only:
/// myparts.ge lists no labour prices, so labour ranges stay in <see cref="RepairCostService"/>.
///
/// To refresh: repeat the search on myparts.ge (/ka/nawilebi/?keyword=...) and edit the rows below; nothing else
/// in the app needs to change. <c>Sample</c> tells how many listings the row is based on — rows with a
/// small sample are only a rough guide.
/// </summary>
public static class MarketPartPrices
{
    public const string Source = "myparts.ge";
    public const string RecordedOn = "2026-10-09";

    public sealed record Part(
        string Key,
        string English,
        string Georgian,
        int NewLow,
        int NewTypical,
        int NewHigh,
        int UsedLow,
        int UsedTypical,
        int UsedHigh,
        int Sample);

    public static readonly IReadOnlyList<Part> All = new[]
    {
        new Part("catalyst",      "Catalytic converter",            "კატალიზატორი",                100, 200, 350,   0,   0,    0, 30),
        new Part("o2_sensor",     "Oxygen (lambda) sensor",         "ლამბდა ზონდი",                120, 130, 200, 250, 250,  250, 30),
        new Part("spark_plugs",   "Spark plugs (listing price)",    "აალების სანთლები",             30,  40,  40,   0,   0,    0, 30),
        new Part("ignition_coil", "Ignition coil",                  "აალების კოჭა",                 80,  80, 250,  40,  40,   40, 30),
        new Part("thermostat",    "Thermostat",                     "თერმოსტატი",                   60,  60,  80,   0,   0,    0, 30),
        new Part("throttle_body", "Throttle body",                  "დროსელის სხეული",             180, 447, 600, 100, 150,  250, 30),
        new Part("fuel_pump",     "Fuel pump",                      "ბენზინის ტუმბო",               60,  80, 150,  80,  80,   80, 18),
        new Part("turbo",         "Turbocharger",                   "ტურბო",                       450, 600, 650, 450, 500,  850, 30),
        new Part("battery",       "Battery (12 V)",                 "აკუმულატორი",                  90, 150, 640,  80, 100,  350, 28),
        new Part("abs_sensor",    "ABS / wheel speed sensor",       "ABS სენსორი",                  50,  65, 120,  30,  40,   50, 30),
        new Part("injector",      "Fuel injector",                  "ინჟექტორი",                    50,  65, 400, 100, 100,  250, 30),
        new Part("vvt_solenoid",  "VVT / VANOS solenoid",           "ვანოსის სოლენოიდი",            90, 195, 270, 100, 150,  150, 12),
        new Part("water_pump",    "Water pump",                     "წყლის ტუმბო",                  80, 120, 400,  60,  60,   60, 30),
        new Part("temp_sensor",   "Temperature sensor",             "ტემპერატურის სენსორი",         30,  40, 117,  30,  50,  200, 24),
        new Part("alternator",    "Alternator",                     "გენერატორი",                  145, 145, 150, 150, 250,  390, 14),
        new Part("starter",       "Starter motor",                  "სტარტერი",                    250, 250, 250, 100, 150,  180, 30),
        new Part("ac_compressor", "A/C compressor",                 "კონდიციონერის კომპრესორი",    800, 950,1200, 200, 250,  300, 30),
        new Part("radiator",      "Radiator",                       "რადიატორი",                   121, 150, 250, 250, 250,  250, 30),
        new Part("oil_pump",      "Oil pump",                       "ზეთის ტუმბო",                 350, 435, 800, 250,1500, 1500, 14),
        new Part("maf",          "Mass air flow (MAF) sensor",    "ჰაერის ნაკადის სენსორი",     110, 120, 200, 120, 120,  150, 11),
        new Part("map",          "Manifold pressure (MAP) sensor", "MAP სენსორი",                 40,  94, 178,  60,  60,   60, 7),
        new Part("crank_sensor", "Crankshaft position sensor",   "ბრუნვის ამთვლელი სენსორი",   35,  75, 150,   0,   0,    0, 8),
        new Part("cam_sensor",   "Camshaft position sensor",      "გამანაწილებელი ლილვის სენსორი", 124, 126, 150,  50,  50,   50, 15),
        new Part("knock_sensor", "Knock sensor",                  "დეტონაციის სენსორი",         100, 100, 150,  50,  50,   50, 8),
        new Part("egr_valve",    "EGR valve (mostly used listings)", "EGR სარქველი",              80, 182, 250,  80, 182,  250, 5),
        new Part("purge_valve",  "EVAP purge valve / canister (used)", "ადსორბერის სარქველი",     50, 100, 150,  50, 100,  150, 4),
        new Part("fuel_cap",     "Fuel / coolant cap",            "ავზის თავსახური",             50,  60, 115,  30,  50,  100, 15),
        new Part("tpms_sensor",  "Tyre pressure (TPMS) sensor",   "საბურავის წნევის სენსორი",    50,  60,  75,  50,  60,   70, 15),
        new Part("ecu",          "Engine computer (ECU, used)",   "ძრავის კომპიუტერი",          150, 250, 300, 150, 250,  300, 14),
        new Part("wheel_bearing", "Wheel bearing / hub",          "მორგვის საკისარი",            30,  75, 133,  68,  68,   68, 15),
        new Part("airbag",       "Airbag module",                 "უსაფრთხოების ბალიში",        100, 326,1050, 300, 500,  500, 14),
        new Part("glow_plug",    "Glow plug",                     "გამახურებელი სანთელი",        16,  18,  18,   0,   0,    0, 3),
        new Part("trans_solenoid", "Transmission solenoid",          "კოლოფის სოლენოიდი",           90, 195, 270, 150, 200,  200, 13),
        new Part("trans_fluid",  "Transmission oil",               "კოლოფის ზეთი",                38,  90, 350,  60, 250,  250, 14),
        new Part("trans_filter", "Transmission filter",            "კოლოფის ფილტრი",              99, 130, 200,   0,   0,    0, 15),
        new Part("trans_sensor", "Transmission speed / range sensor", "კოლოფის სენსორი",         149, 149, 149,   0,   0,    0, 1),
        new Part("tcm",          "Transmission control unit (TCM)", "კოლოფის კომპიუტერი",         200, 250,1300, 200, 250, 1301, 15),
        new Part("trans_unit",   "Gearbox, complete (used)",       "გადაცემათა კოლოფი",          300, 800,1041, 300, 800, 1041, 15),
        new Part("body_module",  "Body / comfort / light control module (used)", "კომფორტის ბლოკი", 250, 450, 550, 250, 450,  550, 7),
        new Part("cluster",      "Instrument cluster (used)",      "ხელსაწყოთა პანელი",         1500,1500,1500,1500,1500, 1500, 2),
        new Part("abs_unit",     "ABS pump / unit",                "ABS პომპა (ბლოკი)",         216,1100,1400, 216,1100, 1400, 18),
    };

    public static Part? Find(string key) =>
        All.FirstOrDefault(part => part.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Part-price range in ₾ for the estimate: from the cheapest realistic option (a used part when the market has
    /// enough of them, otherwise the low new price) up to the high new price.
    /// </summary>
    public static (int Min, int Max) Range(string key)
    {
        // "none": the repair often needs no part at all (wiring, connector, ground, software).
        if (key.Equals("none", StringComparison.OrdinalIgnoreCase))
            return (0, 0);

        var part = Find(key) ?? throw new ArgumentException("Unknown part: " + key, nameof(key));
        var min = part.UsedLow > 0 && part.UsedLow < part.NewLow ? part.UsedLow : part.NewLow;
        return (min, Math.Max(part.NewHigh, min));
    }

    /// <summary>Union of the ranges of several parts (a code that can be caused by any of them).</summary>
    public static (int Min, int Max) Range(params string[] keys)
    {
        var ranges = keys.Select(Range).ToArray();
        return (ranges.Min(r => r.Min), ranges.Max(r => r.Max));
    }

    /// <summary>The same table as text for the AI prompt, so its part prices follow the real market.</summary>
    public static string PromptReference()
    {
        var text = new StringBuilder();
        text.AppendLine($"Georgian market part prices in GEL from real {Source} listings ({RecordedOn}); new = low–high, used = typical:");
        foreach (var part in All)
        {
            var used = part.UsedTypical > 0 ? $"; used ~{part.UsedTypical}" : string.Empty;
            text.AppendLine($"- {part.English}: new {part.NewLow}–{part.NewHigh}{used}");
        }

        text.AppendLine("When a part above is a likely cause, keep partMinGel/partMaxGel inside these real prices; do not inflate them.");
        text.AppendLine("Labour is not in this table: use typical independent-garage labour in Georgia.");
        return text.ToString();
    }
}
