namespace CarScanner.Services;

/// <summary>
/// Maps a car's manufacturer (as the VIN decoder or the user gives it) to the
/// make names used in the bundled OBDb catalog, with related brands that
/// share ECUs and identifiers. The car's own make comes first.
/// </summary>
public static class CatalogMakes
{
    // (keys to recognise the manufacturer, catalog make names)
    private static readonly (string[] Keys, string[] Makes)[] Families =
    {
        (new[] { "mercedes", "benz", "smart", "daimler" }, new[] { "Mercedes", "smart" }),
        (new[] { "chevrolet", "chevy", "gmc", "buick", "cadillac", "holden", "generalmotors", "gm" }, new[] { "Chevrolet", "GMC", "Buick", "Cadillac" }),
        (new[] { "ford", "lincoln", "mercury" }, new[] { "Ford" }),
        (new[] { "volkswagen", "vw", "audi", "seat", "cupra", "skoda", "porsche" }, new[] { "Volkswagen", "Audi", "Seat", "Cupra", "Skoda", "Porsche" }),
        (new[] { "bmw", "bayerische", "mini", "rolls" }, new[] { "BMW", "MINI", "Rolls" }),
        (new[] { "daihatsu" }, new[] { "Daihatsu", "Toyota" }),
        (new[] { "toyota", "lexus", "scion" }, new[] { "Toyota", "Lexus", "Scion" }),
        (new[] { "hyundai", "kia", "genesis" }, new[] { "Hyundai", "Kia", "Genesis" }),
        (new[] { "nissan", "infiniti", "datsun" }, new[] { "Nissan", "NissanInfiniti", "INFINITI" }),
        (new[] { "renault", "dacia" }, new[] { "Renault", "Dacia" }),
        (new[] { "mitsubishi" }, new[] { "Mitsubishi" }),
        (new[] { "honda", "acura" }, new[] { "Honda", "Acura" }),
        (new[] { "mazda" }, new[] { "Mazda" }),
        (new[] { "subaru", "fujiheavy" }, new[] { "Subaru" }),
        (new[] { "suzuki" }, new[] { "Suzuki" }),
        (new[] { "volvo", "polestar" }, new[] { "Volvo", "Polestar" }),
        (new[] { "peugeot", "citroen", "ds", "opel", "vauxhall" }, new[] { "Peugeot", "Citroen", "VauxhallOpel" }),
        (new[] { "fiat", "alfa", "abarth", "lancia" }, new[] { "FIAT", "Alfa" }),
        (new[] { "jeep", "dodge", "ram", "chrysler" }, new[] { "Jeep", "Dodge", "Ram", "Chrysler" }),
        (new[] { "jaguar", "landrover", "land" }, new[] { "Jaguar", "Land" }),
        (new[] { "mg" }, new[] { "MG" }),
        (new[] { "haval", "greatwall" }, new[] { "Haval" }),
        (new[] { "lotus" }, new[] { "Lotus" }),
        (new[] { "aiways" }, new[] { "Aiways" })
    };

    /// <summary>True when the manufacturer belongs to a known catalog family.</summary>
    public static bool Known(string? manufacturer) => Find(manufacturer) is not null;

    /// <summary>Catalog makes for the manufacturer (own make first); the trimmed input when unknown.</summary>
    public static IReadOnlyList<string> For(string? manufacturer)
    {
        var value = Normalize(manufacturer);
        var family = Find(manufacturer);
        if (family is null)
            return string.IsNullOrWhiteSpace(manufacturer) ? Array.Empty<string>() : new[] { manufacturer.Trim() };

        // The make that matches the car best comes first.
        return family.Value.Makes
            .OrderByDescending(make => value.Contains(Normalize(make)) || Normalize(make).Contains(value))
            .ToArray();
    }

    private static (string[] Keys, string[] Makes)? Find(string? manufacturer)
    {
        var value = Normalize(manufacturer);
        if (value.Length == 0)
            return null;

        foreach (var family in Families)
        {
            foreach (var key in family.Keys)
            {
                var normalizedKey = Normalize(key);
                // Short keys (vw, mg, ds, gm, ram) must match the whole name or its first word.
                var match = normalizedKey.Length <= 3
                    ? value == normalizedKey || FirstWord(manufacturer) == normalizedKey
                    : value.Contains(normalizedKey);
                if (match)
                    return family;
            }
        }

        return null;
    }

    private static string FirstWord(string? value) =>
        Normalize((value ?? string.Empty).Trim().Split(' ', '-', '/').FirstOrDefault());

    // Lower case, letters and digits only, accents removed (Škoda → skoda, Citroën → citroen).
    private static string Normalize(string? value) =>
        new string((value ?? string.Empty)
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant)
            .Where(char.IsLetterOrDigit)
            .ToArray());
}
