using CarScanner.Models;

namespace CarScanner.Services;

/// <summary>
/// Stores the driver's chosen brand before an OBD connection is made.
/// A selection never fabricates OEM support: vehicle features remain enabled
/// only when the underlying diagnostic service supports them.
/// </summary>
public sealed class VehicleBrandProfileService
{
    private const string SelectedBrandKey = "selected_vehicle_brand";

    private static readonly IReadOnlyList<VehicleBrandProfile> Profiles =
    [
        new("abarth", "Abarth", "ABARTH", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("acura", "Acura", "ACURA", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("alfa-romeo", "Alfa Romeo", "ALFA", "#DC2626", "Generic OBD-II diagnostics"),
        new("audi", "Audi", "AUDI", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("bmw", "BMW", "BMW", "#3B82F6", "Generic OBD-II diagnostics"),
        new("bentley", "Bentley", "BENTLEY", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("baic", "BAIC", "BAIC", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("buick", "Buick", "BUICK", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("byd", "BYD", "BYD", "#EF4444", "Generic OBD-II diagnostics"),
        new("cadillac", "Cadillac", "CADILLAC", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("chery", "Chery", "CHERY", "#EF4444", "Generic OBD-II diagnostics"),
        new("chevrolet", "Chevrolet", "CHEVROLET", "#FBBF24", "Generic OBD-II diagnostics"),
        new("chrysler", "Chrysler", "CHRYSLER", "#94A3B8", "Generic OBD-II diagnostics"),
        new("cupra", "Cupra", "CUPRA", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("citroen", "Citroën", "CITROËN", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("dacia", "Dacia", "DACIA", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("dodge", "Dodge", "DODGE", "#DC2626", "Generic OBD-II diagnostics"),
        new("dongfeng", "Dongfeng", "DONGFENG", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("ds", "DS Automobiles", "DS", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("daewoo", "Daewoo", "DAEWOO", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("ferrari", "Ferrari", "FERRARI", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("fiat", "Fiat", "FIAT", "#DC2626", "Generic OBD-II diagnostics"),
        new("ford", "Ford", "FORD", "#2563EB", "Generic OBD-II diagnostics"),
        new("genesis", "Genesis", "GENESIS", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("geely", "Geely", "GEELY", "#38BDF8", "Generic OBD-II diagnostics"),
        new("gmc", "GMC", "GMC", "#DC2626", "Generic OBD-II diagnostics"),
        new("haval", "Haval", "HAVAL", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("hongqi", "Hongqi", "HONGQI", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("honda", "Honda", "HONDA", "#DC2626", "Generic OBD-II diagnostics"),
        new("hyundai", "Hyundai", "HYUNDAI", "#60A5FA", "Generic OBD-II diagnostics"),
        new("infiniti", "Infiniti", "INFINITI", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("isuzu", "Isuzu", "ISUZU", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("jaguar", "Jaguar", "JAGUAR", "#22C55E", "Generic OBD-II diagnostics"),
        new("jac", "JAC", "JAC", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("jeep", "Jeep", "JEEP", "#22C55E", "Generic OBD-II diagnostics"),
        new("kia", "Kia", "KIA", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("land-rover", "Land Rover", "LAND ROVER", "#22C55E", "Generic OBD-II diagnostics"),
        new("lada", "Lada", "LADA", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("lamborghini", "Lamborghini", "LAMBORGHINI", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("lancia", "Lancia", "LANCIA", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("lexus", "Lexus", "LEXUS", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("lincoln", "Lincoln", "LINCOLN", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("lucid", "Lucid", "LUCID", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("maserati", "Maserati", "MASERATI", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("mclaren", "McLaren", "MCLAREN", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("mazda", "Mazda", "MAZDA", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("mercedes", "Mercedes-Benz", "MB", "#94A3B8", "Generic OBD-II diagnostics"),
        new("mg", "MG", "MG", "#DC2626", "Generic OBD-II diagnostics"),
        new("mini", "MINI", "MINI", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("mitsubishi", "Mitsubishi", "MITSUBISHI", "#DC2626", "Generic OBD-II diagnostics"),
        new("nissan", "Nissan", "NISSAN", "#94A3B8", "Generic OBD-II diagnostics"),
        new("nio", "NIO", "NIO", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("opel", "Opel", "OPEL", "#FBBF24", "Generic OBD-II diagnostics"),
        new("peugeot", "Peugeot", "PEUGEOT", "#60A5FA", "Generic OBD-II diagnostics"),
        new("porsche", "Porsche", "PORSCHE", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("polestar", "Polestar", "POLESTAR", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("ram", "Ram", "RAM", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("renault", "Renault", "RENAULT", "#FBBF24", "Generic OBD-II diagnostics"),
        new("saab", "Saab", "SAAB", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("rivian", "Rivian", "RIVIAN", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("rolls-royce", "Rolls-Royce", "ROLLS-ROYCE", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("seat", "SEAT", "SEAT", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("skoda", "Škoda", "ŠKODA", "#22C55E", "Generic OBD-II diagnostics"),
        new("ssangyong", "KGM / SsangYong", "KGM", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("subaru", "Subaru", "SUBARU", "#2563EB", "Generic OBD-II diagnostics"),
        new("suzuki", "Suzuki", "SUZUKI", "#60A5FA", "Generic OBD-II diagnostics"),
        new("tesla", "Tesla", "TESLA", "#EF4444", "Generic OBD-II diagnostics"),
        new("toyota", "Toyota", "TOYOTA", "#EF4444", "Generic OBD-II diagnostics"),
        new("volkswagen", "Volkswagen", "VW", "#38BDF8", "Generic OBD-II diagnostics"),
        new("volvo", "Volvo", "VOLVO", "#60A5FA", "Generic OBD-II diagnostics"),
        new("smart", "Smart", "SMART", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("uaz", "UAZ", "UAZ", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("zeekr", "Zeekr", "ZEEKR", "#E5E7EB", "Generic OBD-II diagnostics"),
        new("other", "სხვა მარკა", "OBD", "#22C55E", "Generic OBD-II engine diagnostics მზადაა")
    ];

    private static readonly string[] PopularFirst =
    [
        "toyota", "mercedes", "bmw", "lexus", "honda", "hyundai",
        "kia", "ford", "nissan", "volkswagen", "mazda", "chevrolet",
        "cadillac", "fiat"
    ];

    public event EventHandler<VehicleBrandProfile>? Changed;

    public IReadOnlyList<VehicleBrandProfile> Available => Profiles
        .OrderBy(profile =>
        {
            var index = Array.IndexOf(PopularFirst, profile.Id);
            return index < 0 ? int.MaxValue : index;
        })
        .ThenBy(profile => profile.DisplayName)
        .ToArray();

    public VehicleBrandProfile Current
    {
        get
        {
            var id = Preferences.Default.Get(SelectedBrandKey, "other");
            return Profiles.FirstOrDefault(x => x.Id == id) ?? Profiles[^1];
        }
    }

    public string GetVerificationMessage(string? detectedManufacturer)
    {
        if (!HasDetectedManufacturer(detectedManufacturer))
            return $"არჩეული პროფილი: {Current.DisplayName} — VIN-ით დადასტურდება";

        return IsCurrentBrand(detectedManufacturer)
            ? $"{Current.DisplayName} პროფილი VIN-ით დადასტურდა"
            : $"VIN მიუთითებს: {detectedManufacturer} — მარკის გადარჩევა რეკომენდებულია";
    }

    public bool IsCurrentBrand(string? detectedManufacturer)
    {
        return VehicleProfileResolutionEngine.MatchesBrand(Current, detectedManufacturer);
    }

    public void Select(string brandId)
    {
        var profile = Profiles.FirstOrDefault(x => x.Id == brandId);
        if (profile is null)
            throw new ArgumentOutOfRangeException(nameof(brandId));

        Preferences.Default.Set(SelectedBrandKey, profile.Id);
        Changed?.Invoke(this, profile);
    }

    private static bool HasDetectedManufacturer(string? manufacturer) =>
        !string.IsNullOrWhiteSpace(manufacturer)
        && !manufacturer.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
        && !manufacturer.Equals("უცნობია", StringComparison.OrdinalIgnoreCase);

}
