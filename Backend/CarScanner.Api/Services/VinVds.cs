namespace CarScanner.Api.Services;

/// <summary>
/// Offline model / model-year decoding for VINs that NHTSA vPIC does not
/// know (cars built for Europe, Asia, etc.). Only manufacturer schemes that
/// are publicly documented and stable are used:
///  • Volkswagen group (VW, Audi, Škoda, SEAT): VIN positions 7-8 = model code.
///  • Mercedes-Benz: positions 4-6 = model series (212 = E-Class, ...).
///  • Renault, Opel: model letter in the VDS (tables after vininfo, BSD-3,
///    Copyright (c) 2018-2026 Igor Starikov).
///  • Model year (position 10) only for makers that encode it there outside
///    North America too. Mercedes, Renault, PSA, Fiat, Ford Europe, Nissan
///    and Japanese-market VINs do not, so no year is guessed for them.
/// Values are only filled when the online decoder returned nothing.
/// </summary>
public static class VinVds
{
    public sealed record Result(string? Model, string? Series, int? Year);

    public static Result Decode(string vin, string? make)
    {
        if (string.IsNullOrWhiteSpace(vin) || vin.Length != 17)
            return new Result(null, null, null);

        vin = vin.ToUpperInvariant();
        var wmi = vin[..3];
        var year = YearFromPosition10(vin, wmi);

        string? model = null, series = null;
        if (IsVwGroup(wmi))
        {
            var code = vin.Substring(6, 2);
            var table = wmi switch
            {
                "WAU" or "WA1" or "WUA" or "TRU" => Audi,
                "TMB" => Skoda,
                "VSS" => Seat,
                _ => Volkswagen
            };
            if (table.TryGetValue(code, out var name))
            {
                // "16" is the early Jetta and also the 2012+ Beetle.
                if (code == "16" && table == Volkswagen)
                    name = year is >= 2011 ? "Beetle (A5)" : "Jetta";
                model = name;
                series = code;
            }
        }
        else if (IsMercedes(wmi))
        {
            var code = vin.Substring(3, 3);
            if (MercedesSeries.TryGetValue(code, out var name))
            {
                model = name;
                series = code;
            }
        }
        else if (wmi is "VF1" or "UU1" or "X7L" && Renault.TryGetValue(vin[4], out var r))
        {
            model = r;
        }
        else if (wmi is "W0L" or "W0V" && Opel.TryGetValue(vin[3], out var o))
        {
            model = o;
        }

        return new Result(model, series, year);
    }

    private static bool IsVwGroup(string wmi) =>
        wmi is "WVW" or "WVG" or "WV1" or "WV2" or "WAU" or "WA1" or "WUA" or "TRU" or "TMB" or "VSS" or "VWV" or "XW8";

    private static bool IsMercedes(string wmi) =>
        wmi is "WDB" or "WDD" or "WDC" or "WDF" or "W1K" or "W1N" or "W1V" or "W1W" or "WMX";

    // Makers that put the model year in position 10 worldwide.
    private static readonly string[] YearAtTen =
    {
        "WVW", "WVG", "WV1", "WV2", "WAU", "WA1", "WUA", "TRU", "TMB", "VSS", "VWV", "XW8", "WP0", "WP1",
        "WBA", "WBS", "WBY", "WBX", "WMW", "YV1", "YV4", "LYV", "W0L", "W0V", "SAJ", "SAL", "SAD",
        "KMH", "KM8", "KMF", "KMT", "KNA", "KND", "TMA", "TMK", "U5Y", "U6Y", "NLH", "MAL", "MZB", "Z94",
        "LRW", "LPS", "SHH", "SHS"
    };

    /// <summary>Position-10 year, picking the latest cycle that is not in the future.</summary>
    public static int? YearFromPosition10(string vin, string wmi)
    {
        var chinese = wmi[0] == 'L';
        if (!chinese && Array.IndexOf(YearAtTen, wmi) < 0)
            return null;

        const string letters = "ABCDEFGHJKLMNPRSTVWXY123456789";
        var index = letters.IndexOf(vin[9]);
        if (index < 0)
            return null;

        var year = 1980 + index;
        var latest = DateTime.UtcNow.Year + 1;
        while (year + 30 <= latest)
            year += 30;
        return year;
    }

    private static readonly Dictionary<string, string> Volkswagen = new(StringComparer.Ordinal)
    {
        // Source: Club VeeDub VW VIN reference (positions 7-8).
        ["13"] = "Scirocco", ["16"] = "Jetta", ["17"] = "Golf I", ["19"] = "Golf II", ["1C"] = "New Beetle",
        ["1E"] = "Golf III Cabriolet", ["1F"] = "Eos", ["1G"] = "Golf / Jetta II", ["1H"] = "Golf III / Vento",
        ["1J"] = "Golf IV / Bora", ["1K"] = "Golf V–VI / Jetta", ["1T"] = "Touran", ["1Y"] = "New Beetle Cabriolet",
        ["2E"] = "Crafter", ["2H"] = "Amarok", ["2K"] = "Caddy III", ["3A"] = "Passat B3/B4",
        ["3B"] = "Passat B5", ["3C"] = "Passat B6–B8 / CC", ["3D"] = "Phaeton", ["3H"] = "Arteon",
        ["5K"] = "Golf VI / Jetta", ["5M"] = "Golf Plus", ["5N"] = "Tiguan", ["5Z"] = "Fox",
        ["6K"] = "Polo Classic", ["6N"] = "Polo III", ["6R"] = "Polo V", ["6X"] = "Lupo", ["70"] = "Transporter T4",
        ["7H"] = "Transporter T5 / T6.1", ["7J"] = "Transporter T6", ["7L"] = "Touareg I", ["7M"] = "Sharan I",
        ["7P"] = "Touareg II", ["9C"] = "New Beetle", ["9N"] = "Polo IV", ["A1"] = "T-Roc", ["AA"] = "up!",
        ["AU"] = "Golf VII", ["AW"] = "Polo VI", ["C1"] = "T-Cross", ["CD"] = "Golf VIII", ["SK"] = "Caddy IV",
        ["SY"] = "Crafter II",
    };

    private static readonly Dictionary<string, string> Audi = new(StringComparer.Ordinal)
    {
        // Audi codes in European VINs (8K, 4G, F4 ...) plus the US-market F-codes.
        ["8L"] = "A3 (8L)", ["8P"] = "A3 (8P)", ["8V"] = "A3 (8V)", ["8Y"] = "A3 (8Y)",
        ["8E"] = "A4 (B6/B7)", ["8K"] = "A4 (B8)", ["8W"] = "A4 (B9)", ["8T"] = "A5 (8T)", ["8F"] = "A5 Cabriolet (8F)",
        ["F5"] = "A5 (F5)", ["4B"] = "A6 (C5)", ["4F"] = "A6 (C6)", ["4G"] = "A6 / A7 (C7)", ["4A"] = "A6 (C8)",
        ["4K"] = "A6 / A7 (C8)", ["4D"] = "A8 (D2)", ["4E"] = "A8 (D3)", ["4H"] = "A8 (D4)", ["4N"] = "A8 (D5)",
        ["8R"] = "Q5 (8R)", ["FY"] = "Q5 (FY)", ["4L"] = "Q7 (4L)", ["4M"] = "Q7 / Q8 (4M)", ["8U"] = "Q3 (8U)",
        ["F3"] = "Q3 (F3)", ["GA"] = "Q2", ["8X"] = "A1 (8X)", ["GB"] = "A1 (GB)", ["8N"] = "TT (8N)",
        ["8J"] = "TT (8J)", ["42"] = "R8 (42)", ["4S"] = "R8 (4S)", ["GE"] = "e-tron", ["F4"] = "A4 (B9)", ["F2"] = "A6 / A7 (C8)", ["F8"] = "A8 (D5)", ["F1"] = "Q8", ["GY"] = "A3 (8Y)",
        ["F7"] = "Q7 (4M)", ["FA"] = "A8 (4E)", ["FB"] = "A6 (4F)", ["FC"] = "A6 / A7 (4G)", ["FD"] = "A8 (4H)",
        ["FE"] = "Q7 (4L)", ["FF"] = "A3 (8V)", ["FG"] = "R8 (42)", ["FH"] = "A5 Cabriolet (8F)", ["FK"] = "TT (8J)",
        ["FL"] = "A4 (B8)", ["FM"] = "A3 (8P)", ["FP"] = "Q5 (8R)", ["FR"] = "A5 (8T)", ["FS"] = "Q3 (8U)",
        ["FV"] = "TT (FV)",
    };

    private static readonly Dictionary<string, string> Skoda = new(StringComparer.Ordinal)
    {
        ["1U"] = "Octavia I", ["1Z"] = "Octavia II", ["5E"] = "Octavia III", ["NE"] = "Octavia III", ["NX"] = "Octavia IV",
        ["3U"] = "Superb I", ["3T"] = "Superb II", ["3V"] = "Superb III", ["NP"] = "Superb III", ["6Y"] = "Fabia I",
        ["5J"] = "Fabia II / Roomster", ["NJ"] = "Fabia III", ["PJ"] = "Fabia IV", ["5L"] = "Yeti",
        ["NH"] = "Rapid", ["NS"] = "Kodiaq", ["NU"] = "Karoq", ["NW"] = "Scala / Kamiq",
    };

    private static readonly Dictionary<string, string> Seat = new(StringComparer.Ordinal)
    {
        ["1M"] = "León I / Toledo II", ["1P"] = "León II", ["5F"] = "León III", ["KL"] = "León IV",
        ["6L"] = "Ibiza III", ["6J"] = "Ibiza IV", ["KJ"] = "Ibiza V / Arona", ["5P"] = "Altea / Toledo III",
        ["7N"] = "Alhambra II",
    };

    private static readonly Dictionary<string, string> MercedesSeries = new(StringComparer.Ordinal)
    {
        // Model series = VIN positions 4-6.
        ["168"] = "A-Class (168)", ["169"] = "A-Class (169)", ["176"] = "A-Class (176)", ["177"] = "A-Class (177)",
        ["245"] = "B-Class (245)", ["246"] = "B-Class (246)", ["247"] = "B-Class / GLB (247)",
        ["117"] = "CLA (117)", ["118"] = "CLA (118)", ["156"] = "GLA (156)",
        ["202"] = "C-Class (202)", ["203"] = "C-Class (203)", ["204"] = "C-Class / GLK (204)", ["205"] = "C-Class (205)",
        ["206"] = "C-Class (206)", ["124"] = "E-Class (124)", ["210"] = "E-Class (210)", ["211"] = "E-Class (211)",
        ["207"] = "E-Class Coupé / Cabrio (207)", ["212"] = "E-Class (212)", ["213"] = "E-Class (213)",
        ["238"] = "E-Class Coupé / Cabrio (238)", ["214"] = "E-Class (214)", ["209"] = "CLK (209)", ["208"] = "CLK (208)",
        ["219"] = "CLS (219)", ["218"] = "CLS (218)", ["257"] = "CLS (257)", ["140"] = "S-Class (140)",
        ["220"] = "S-Class (220)", ["221"] = "S-Class (221)", ["222"] = "S-Class (222)", ["223"] = "S-Class (223)",
        ["216"] = "CL (216)", ["215"] = "CL (215)", ["217"] = "S-Class Coupé (217)", ["163"] = "M-Class (163)",
        ["164"] = "M-Class / GL (164)", ["166"] = "M-Class / GLE / GL / GLS (166)", ["167"] = "GLE / GLS (167)",
        ["292"] = "GLE Coupé (292)", ["253"] = "GLC (253)", ["254"] = "GLC (254)",
        ["463"] = "G-Class (463)", ["251"] = "R-Class (251)", ["171"] = "SLK (171)", ["172"] = "SLK / SLC (172)",
        ["230"] = "SL (230)", ["231"] = "SL (231)", ["232"] = "SL (232)", ["197"] = "SLS AMG (197)",
        ["190"] = "AMG GT (190)", ["290"] = "AMG GT 4-door (290)", ["243"] = "EQA (243)", ["293"] = "EQC (293)",
        ["295"] = "EQE (295)", ["297"] = "EQS (297)", ["294"] = "EQE SUV (294)", ["296"] = "EQS SUV (296)",
        ["639"] = "Vito / Viano (639)", ["447"] = "Vito / V-Class (447)", ["906"] = "Sprinter (906)",
        ["907"] = "Sprinter (907)", ["910"] = "Sprinter (910)", ["414"] = "Vaneo (414)", ["470"] = "X-Class (470)",
    };

    private static readonly Dictionary<char, string> Renault = new()
    {
        // VDS letter (position 5); only unambiguous entries.
        ['0'] = "Twingo", ['B'] = "Clio II", ['C'] = "Kangoo", ['D'] = "Master", ['G'] = "Laguna II",
        ['H'] = "Master", ['K'] = "Espace IV", ['L'] = "Trafic", ['M'] = "Mégane II", ['P'] = "Modus",
        ['S'] = "Logan / Sandero / Duster", ['Y'] = "Koleos",
    };

    private static readonly Dictionary<char, string> Opel = new()
    {
        // VDS letter (position 4).
        ['F'] = "Agila", ['G'] = "Insignia", ['J'] = "Mokka", ['L'] = "Antara", ['M'] = "Movano",
        ['P'] = "Astra J / Zafira C",
    };
}
