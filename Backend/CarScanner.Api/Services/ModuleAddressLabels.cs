namespace CarScanner.Api.Services;

/// <summary>
/// Display-only labels (ECM, ABS, SRS, …) for a TX/RX CAN id pair, by brand. This mirrors the address tables in
/// the mobile app's module scan (Services/OemDiagnostics/ModuleDtcScanService.cs) — kept here as an independent
/// copy because the mobile app and this API are separate deployables that do not share code. It exists only to
/// label an already-collected, already-sanitised observation for the admin page; it is never used to decide what
/// the app may ask a real vehicle, and it never turns an address into a claim that a feature is supported.
/// Keep this table in sync by hand if the mobile one changes.
/// </summary>
public static class ModuleAddressLabels
{
    public enum Family
    {
        Unknown,
        Gm,
        Ford,
        Mazda,
        HyundaiKia,
        Toyota,
        Honda,
        VwGroup,
        Nissan,
        Subaru,
        Stellantis,
        Chrysler,
        Renault,
        Bmw
    }

    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["ECM"] = "ძრავი", ["TCM"] = "გადაცემათა კოლოფი", ["ABS"] = "ABS / მუხრუჭები", ["SRS"] = "აირბაგი (SRS)",
        ["BCM"] = "ძარის მოდული (BCM)", ["IPC"] = "ხელსაწყოების დაფა", ["EPS"] = "საჭის გამაძლიერებელი",
        ["HVAC"] = "კლიმატ-კონტროლი", ["HYB"] = "ჰიბრიდის სისტემა", ["BMS"] = "მაღალი ძაბვის ბატარეა",
        ["GW"] = "გეითვეი (Gateway)", ["RADIO"] = "მულტიმედია", ["ADAS"] = "რადარი / კამერა",
        ["KEY"] = "გასაღები / იმობილაიზერი", ["PARK"] = "პარკინგის სისტემა", ["OBD"] = "OBD მოდული", ["MOD"] = "მოდული"
    };

    private static readonly (int Tx, int Rx, string Id)[] StandardObd =
    {
        (0x7E0, 0x7E8, "ECM"), (0x7E1, 0x7E9, "TCM"), (0x7E2, 0x7EA, "OBD"), (0x7E3, 0x7EB, "OBD"),
        (0x7E4, 0x7EC, "OBD"), (0x7E5, 0x7ED, "OBD"), (0x7E6, 0x7EE, "OBD"), (0x7E7, 0x7EF, "OBD")
    };

    private static readonly Dictionary<Family, (int Tx, int Rx, string Id)[]> BrandModules = new()
    {
        [Family.Gm] = new[]
        {
            (0x7E2, 0x7EA, "TCM"), (0x7E4, 0x7EC, "HYB"), (0x7E7, 0x7EF, "BMS"), (0x241, 0x641, "BCM"),
            (0x242, 0x642, "MOD"), (0x243, 0x643, "ABS"), (0x244, 0x644, "MOD"), (0x245, 0x645, "MOD"),
            (0x246, 0x646, "MOD"), (0x247, 0x647, "SRS"), (0x248, 0x648, "MOD"), (0x249, 0x649, "MOD"),
            (0x24A, 0x64A, "MOD"), (0x24B, 0x64B, "MOD"), (0x24C, 0x64C, "MOD"), (0x24D, 0x64D, "MOD"),
            (0x24E, 0x64E, "MOD"), (0x24F, 0x64F, "MOD"), (0x250, 0x650, "MOD"), (0x251, 0x651, "HVAC"),
            (0x252, 0x652, "MOD"), (0x253, 0x653, "MOD"), (0x254, 0x654, "MOD"), (0x255, 0x655, "MOD"),
            (0x256, 0x656, "MOD"), (0x257, 0x657, "MOD"), (0x258, 0x658, "MOD"), (0x259, 0x659, "MOD"),
            (0x25A, 0x65A, "MOD"), (0x25B, 0x65B, "MOD"), (0x25C, 0x65C, "MOD"), (0x25D, 0x65D, "MOD")
        },
        [Family.Ford] = new[]
        {
            (0x760, 0x768, "ABS"), (0x737, 0x73F, "SRS"), (0x726, 0x72E, "BCM"), (0x720, 0x728, "IPC"),
            (0x730, 0x738, "EPS"), (0x733, 0x73B, "HVAC"), (0x716, 0x71E, "GW"), (0x7D0, 0x7D8, "RADIO"),
            (0x727, 0x72F, "RADIO"), (0x706, 0x70E, "ADAS"), (0x764, 0x76C, "ADAS"), (0x736, 0x73E, "PARK"),
            (0x724, 0x72C, "MOD"), (0x740, 0x748, "MOD"), (0x741, 0x749, "MOD")
        },
        [Family.Mazda] = new[]
        {
            (0x760, 0x768, "ABS"), (0x737, 0x73F, "SRS"), (0x726, 0x72E, "BCM"), (0x720, 0x728, "IPC"),
            (0x730, 0x738, "EPS"), (0x733, 0x73B, "HVAC"), (0x7D0, 0x7D8, "RADIO"), (0x706, 0x70E, "ADAS"),
            (0x764, 0x76C, "ADAS")
        },
        [Family.HyundaiKia] = new[]
        {
            (0x7D1, 0x7D9, "ABS"), (0x7D2, 0x7DA, "SRS"), (0x7D4, 0x7DC, "EPS"), (0x7A0, 0x7A8, "BCM"),
            (0x770, 0x778, "BCM"), (0x7C6, 0x7CE, "IPC"), (0x7B3, 0x7BB, "HVAC"), (0x7C4, 0x7CC, "ADAS"),
            (0x7D0, 0x7D8, "ADAS"), (0x7A5, 0x7AD, "KEY"), (0x7C7, 0x7CF, "ADAS")
        },
        [Family.Toyota] = new[]
        {
            (0x7B0, 0x7B8, "ABS"), (0x780, 0x788, "SRS"), (0x7A1, 0x7A9, "EPS"), (0x7C0, 0x7C8, "IPC"),
            (0x7C4, 0x7CC, "HVAC"), (0x791, 0x799, "HYB"), (0x781, 0x789, "PARK"), (0x750, 0x758, "GW"),
            (0x790, 0x798, "ADAS")
        },
        [Family.VwGroup] = new[]
        {
            (0x713, 0x77D, "ABS"), (0x715, 0x77F, "SRS"), (0x712, 0x77C, "EPS"), (0x710, 0x77A, "GW"),
            (0x714, 0x77E, "IPC"), (0x70E, 0x778, "BCM"), (0x746, 0x7B0, "HVAC"), (0x773, 0x7DD, "RADIO"),
            (0x757, 0x7C1, "ADAS"), (0x74F, 0x7B9, "ADAS"), (0x70A, 0x774, "PARK")
        },
        [Family.Nissan] = new[]
        {
            (0x740, 0x760, "ABS"), (0x752, 0x772, "SRS"), (0x745, 0x765, "BCM"), (0x743, 0x763, "IPC"),
            (0x742, 0x762, "EPS"), (0x744, 0x764, "HVAC"), (0x746, 0x766, "KEY")
        },
        [Family.Subaru] = new[]
        {
            (0x7B0, 0x7B8, "ABS"), (0x746, 0x74E, "EPS"), (0x780, 0x788, "SRS")
        }
    };

    public static Family DetectFamily(string? make)
    {
        var m = (make ?? string.Empty).ToUpperInvariant();
        bool Has(params string[] names) => names.Any(n => m.Contains(n, StringComparison.Ordinal));

        if (Has("CHEVROLET", "GMC", "BUICK", "CADILLAC", "GENERAL MOTORS", "HOLDEN", "PONTIAC", "SATURN", "HUMMER")) return Family.Gm;
        if (Has("FORD", "LINCOLN", "MERCURY")) return Family.Ford;
        if (Has("MAZDA")) return Family.Mazda;
        if (Has("HYUNDAI", "KIA", "GENESIS")) return Family.HyundaiKia;
        if (Has("TOYOTA", "LEXUS", "SCION")) return Family.Toyota;
        if (Has("HONDA", "ACURA")) return Family.Honda;
        if (Has("VOLKSWAGEN", "AUDI", "SKODA", "SEAT", "CUPRA", "PORSCHE")) return Family.VwGroup;
        if (Has("NISSAN", "INFINITI")) return Family.Nissan;
        if (Has("SUBARU")) return Family.Subaru;
        if (Has("PEUGEOT", "CITROEN", "OPEL", "VAUXHALL", "FIAT", "ALFA", "STELLANTIS")) return Family.Stellantis;
        if (Has("CHRYSLER", "DODGE", "JEEP", "RAM")) return Family.Chrysler;
        if (Has("RENAULT", "DACIA")) return Family.Renault;
        if (Has("BMW", "MINI")) return Family.Bmw;
        return Family.Unknown;
    }

    /// <summary>
    /// "ECM"/"ABS"/… for a TX/RX pair of this make, or null when the pair is not in the table. A named brand
    /// module (e.g. GM's 0x7E4 = hybrid system) wins over the generic standard-OBD "OBD" label for the same
    /// address — matching ModuleDtcScanService's own "brand/catalog names win over OBD/MOD" rule.
    /// </summary>
    public static string? ModuleId(string? make, int tx, int rx)
    {
        string? brandHit = null;
        var family = DetectFamily(make);
        if (family != Family.Unknown && BrandModules.TryGetValue(family, out var table))
            foreach (var candidate in table)
                if (candidate.Tx == tx && candidate.Rx == rx) { brandHit = candidate.Id; break; }

        if (brandHit is not null && brandHit != "MOD")
            return brandHit;

        foreach (var candidate in StandardObd)
            if (candidate.Tx == tx && candidate.Rx == rx)
                return candidate.Id;

        return brandHit;
    }

    /// <summary>Georgian name for a TX/RX pair of this make, or "" when unknown (shown as "Unclassified").</summary>
    public static string Label(string? make, int tx, int rx)
    {
        var id = ModuleId(make, tx, rx);
        if (id is null) return string.Empty;
        return Names.TryGetValue(id, out var name) ? name : id;
    }
}
