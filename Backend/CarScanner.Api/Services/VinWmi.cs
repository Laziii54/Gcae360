namespace CarScanner.Api.Services;

/// <summary>
/// Offline decoding of the first VIN characters (WMI, ISO 3780) so cars
/// that NHTSA vPIC does not know (most European, Japanese, Chinese and
/// Indian models) still show their make and country of manufacture.
/// Nothing here is guessed beyond the manufacturer code: model, engine and
/// trim come only from a real decoder.
/// </summary>
public static partial class VinWmi
{
    /// <summary>
    /// Fills Make / Manufacturer / PlantCountry when the decoder returned
    /// nothing for them. Adds "Region" (where the car was built) and
    /// "HistoryCoverage" ("us" for North-American VINs, else "intl").
    /// </summary>
    public static Dictionary<string, string> Enrich(string vin, Dictionary<string, string>? specs)
    {
        var values = specs is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(specs, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(vin) || vin.Length < 3)
            return values;

        vin = vin.ToUpperInvariant();
        var (make, manufacturer) = Manufacturer(vin);
        var country = Country(vin);

        if (!values.ContainsKey("Make") && make is not null)
            values["Make"] = make;
        if (!values.ContainsKey("Manufacturer") && manufacturer is not null)
            values["Manufacturer"] = manufacturer;
        if (!values.ContainsKey("PlantCountry") && country is not null)
            values["PlantCountry"] = country;

        // North-American VINs always encode the model year (position 10).
        if (!values.ContainsKey("ModelYear") && vin.Length == 17 && vin[0] is >= '1' and <= '5' &&
            ModelYear(vin[9], vin[6]) is { } year)
            values["ModelYear"] = year.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Outside North America: model (VW group, Mercedes, Renault, Opel)
        // and model year (makers that use position 10) from the VIN itself.
        if (vin.Length == 17 && vin[0] is not (>= '1' and <= '5'))
        {
            var offline = VinVds.Decode(vin, values.TryGetValue("Make", out var m) ? m : null);
            if (!values.ContainsKey("Model") && offline.Model is not null)
                values["Model"] = offline.Model;
            if (!values.ContainsKey("Series") && offline.Series is not null)
                values["Series"] = offline.Series;
            if (!values.ContainsKey("ModelYear") && offline.Year is { } offlineYear)
                values["ModelYear"] = offlineYear.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        values["HistoryCoverage"] = vin[0] is >= '1' and <= '5' ? "us" : "intl";
        return values;
    }

    /// <summary>Country (or region) of manufacture from the first two characters.</summary>
    public static string? Country(string vin)
    {
        if (vin.Length < 2)
            return null;

        char a = vin[0], b = vin[1];
        bool In(char from, char to) => Index(b) >= Index(from) && Index(b) <= Index(to);

        return a switch
        {
            '1' or '4' or '5' => "United States",
            '2' => "Canada",
            '3' => In('A', 'W') ? "Mexico" : In('X', '7') ? "Costa Rica" : null,
            '6' => "Australia",
            '7' => "New Zealand",
            '8' => In('A', 'E') ? "Argentina" : In('F', 'K') ? "Chile" : In('L', 'R') ? "Ecuador" : In('S', 'W') ? "Peru" : In('X', '2') ? "Venezuela" : null,
            '9' => In('A', 'E') || In('3', '9') ? "Brazil" : In('F', 'K') ? "Colombia" : In('S', 'W') ? "Uruguay" : null,
            'A' => In('A', 'H') ? "South Africa" : null,
            'J' => "Japan",
            'K' => In('L', 'R') ? "South Korea" : In('S', 'T') ? "Kazakhstan" : null,
            'L' => "China",
            'M' => In('A', 'E') ? "India" : In('F', 'K') ? "Indonesia" : In('L', 'R') ? "Thailand" : In('S', 'S') ? "Myanmar" : null,
            'N' => In('L', 'R') ? "Turkey" : In('A', 'E') ? "Iran" : In('F', 'K') ? "Pakistan" : null,
            'P' => In('A', 'E') ? "Philippines" : In('F', 'K') ? "Singapore" : In('L', 'R') ? "Malaysia" : null,
            'R' => In('A', 'E') ? "United Arab Emirates" : In('F', 'K') ? "Taiwan" : In('L', 'R') ? "Vietnam" : null,
            'S' => In('A', 'M') ? "United Kingdom" : In('N', 'T') ? "Germany" : In('U', 'Z') ? "Poland" : In('1', '4') ? "Latvia" : null,
            'T' => In('A', 'H') ? "Switzerland" : In('J', 'P') ? "Czech Republic" : In('R', 'V') ? "Hungary" : In('W', '1') ? "Portugal" : null,
            'U' => In('H', 'M') ? "Denmark" : In('N', 'R') ? "Ireland" : In('U', 'Z') ? "Romania" : In('5', '7') ? "Slovakia" : null,
            'V' => In('A', 'E') ? "Austria" : In('F', 'R') ? "France" : In('S', 'W') ? "Spain" : In('X', '2') ? "Serbia" : In('3', '5') ? "Croatia" : In('6', '0') ? "Estonia" : null,
            'W' => "Germany",
            'X' => In('A', 'E') ? "Bulgaria" : In('F', 'K') ? "Greece" : In('L', 'R') ? "Netherlands" : In('S', 'W') || In('3', '0') ? "Russia" : In('X', '2') ? "Luxembourg" : null,
            'Y' => In('A', 'E') ? "Belgium" : In('F', 'K') ? "Finland" : In('L', 'R') ? "Malta" : In('S', 'W') ? "Sweden" : In('X', '2') ? "Norway" : In('3', '5') ? "Belarus" : In('6', '0') ? "Ukraine" : null,
            'Z' => In('A', 'R') ? "Italy" : In('X', '2') ? "Slovenia" : In('3', '5') ? "Lithuania" : null,
            _ => null
        };
    }

    // Sort order used by ISO 3780 ranges: A…Z then 1…9, 0.
    private static int Index(char c) => c switch
    {
        >= 'A' and <= 'Z' => c - 'A',
        >= '1' and <= '9' => 26 + (c - '1'),
        '0' => 35,
        _ => -1
    };

    /// <summary>Make and manufacturer from the WMI (3 characters, then 2).</summary>
    public static (string? Make, string? Manufacturer) Manufacturer(string vin)
    {
        if (vin.Length >= 3 && Wmi3.TryGetValue(vin[..3], out var three))
            return three;
        if (vin.Length >= 3 && ExtraWmi.TryGetValue(vin[..3], out var extra))
            return (extra, extra);
        if (vin.Length >= 2 && Wmi2.TryGetValue(vin[..2], out var two))
            return two;
        if (vin.Length >= 2 && ExtraWmi.TryGetValue(vin[..2], out var extra2))
            return (extra2, extra2);
        return (null, null);
    }

    private static int? ModelYear(char code, char position7)
    {
        const string letters = "ABCDEFGHJKLMNPRSTVWXY123456789";
        var index = letters.IndexOf(code);
        if (index < 0)
            return null;

        // A letter in position 7 means 2010 or later (North-American rule).
        var year = 1980 + index;
        if (char.IsLetter(position7) && year + 30 <= DateTime.UtcNow.Year + 1)
            year += 30;
        return year;
    }

    private static (string? Make, string? Manufacturer) M(string? make, string? manufacturer) => (make, manufacturer);

    private static readonly Dictionary<string, (string? Make, string? Manufacturer)> Wmi3 = new(StringComparer.Ordinal)
    {
        // Germany
        ["WBA"] = M("BMW", "BMW AG"), ["WBS"] = M("BMW M", "BMW M GmbH"), ["WBY"] = M("BMW i", "BMW AG"),
        ["WBX"] = M("BMW", "BMW AG"), ["WMW"] = M("MINI", "BMW AG"), ["WB1"] = M("BMW Motorrad", "BMW AG"),
        ["WDB"] = M("Mercedes-Benz", "Mercedes-Benz AG"), ["WDD"] = M("Mercedes-Benz", "Mercedes-Benz AG"),
        ["WDC"] = M("Mercedes-Benz", "Mercedes-Benz AG"), ["W1K"] = M("Mercedes-Benz", "Mercedes-Benz AG"),
        ["W1N"] = M("Mercedes-Benz", "Mercedes-Benz AG"), ["W1V"] = M("Mercedes-Benz", "Mercedes-Benz AG"),
        ["WDF"] = M("Mercedes-Benz", "Mercedes-Benz AG"), ["W1W"] = M("Mercedes-Benz", "Mercedes-Benz AG"),
        ["WME"] = M("smart", "Mercedes-Benz AG"), ["WMX"] = M("Mercedes-AMG", "Mercedes-AMG GmbH"),
        ["WVW"] = M("Volkswagen", "Volkswagen AG"), ["WVG"] = M("Volkswagen", "Volkswagen AG"),
        ["WV1"] = M("Volkswagen", "Volkswagen Commercial Vehicles"), ["WV2"] = M("Volkswagen", "Volkswagen Commercial Vehicles"),
        ["WAU"] = M("Audi", "Audi AG"), ["WA1"] = M("Audi", "Audi AG"), ["WUA"] = M("Audi Sport", "Audi Sport GmbH"),
        ["WP0"] = M("Porsche", "Porsche AG"), ["WP1"] = M("Porsche", "Porsche AG"),
        ["W0L"] = M("Opel", "Opel Automobile GmbH"), ["W0V"] = M("Opel", "Opel Automobile GmbH"),
        ["WF0"] = M("Ford", "Ford-Werke GmbH"), ["WMA"] = M("MAN", "MAN Truck & Bus"),
        // France
        ["VF1"] = M("Renault", "Renault"), ["VF6"] = M("Renault Trucks", "Renault Trucks"),
        ["VF3"] = M("Peugeot", "Stellantis"), ["VR3"] = M("Peugeot", "Stellantis"), ["VF7"] = M("Citroën", "Stellantis"),
        ["VR7"] = M("Citroën", "Stellantis"), ["VR1"] = M("DS", "Stellantis"), ["VXK"] = M("Opel", "Stellantis"),
        ["VNK"] = M("Toyota", "Toyota Motor Manufacturing France"), ["UU1"] = M("Dacia", "Renault Group"),
        ["VSS"] = M("SEAT", "SEAT S.A."), ["VSK"] = M("Nissan", "Nissan Motor Ibérica"), ["VS6"] = M("Ford", "Ford España"),
        ["VWV"] = M("Volkswagen", "Volkswagen Navarra"),
        // Italy
        ["ZFA"] = M("Fiat", "Stellantis"), ["ZFC"] = M("Fiat", "Stellantis"), ["ZAR"] = M("Alfa Romeo", "Stellantis"),
        ["ZLA"] = M("Lancia", "Stellantis"), ["ZAC"] = M("Jeep", "Stellantis (Melfi)"), ["ZFF"] = M("Ferrari", "Ferrari S.p.A."),
        ["ZHW"] = M("Lamborghini", "Automobili Lamborghini"), ["ZAM"] = M("Maserati", "Maserati S.p.A."),
        // United Kingdom
        ["SAJ"] = M("Jaguar", "Jaguar Land Rover"), ["SAL"] = M("Land Rover", "Jaguar Land Rover"), ["SAD"] = M("Jaguar", "Jaguar Land Rover"),
        ["SCC"] = M("Lotus", "Lotus Cars"), ["SCF"] = M("Aston Martin", "Aston Martin Lagonda"), ["SCB"] = M("Bentley", "Bentley Motors"),
        ["SCA"] = M("Rolls-Royce", "Rolls-Royce Motor Cars"), ["SBM"] = M("McLaren", "McLaren Automotive"),
        ["SB1"] = M("Toyota", "Toyota Manufacturing UK"), ["SJN"] = M("Nissan", "Nissan UK"), ["SHH"] = M("Honda", "Honda UK"),
        ["SHS"] = M("Honda", "Honda UK"), ["SAR"] = M("Rover", "MG Rover"), ["SFD"] = M("Alexander Dennis", "Alexander Dennis"),
        // Sweden, Czechia, Slovakia, Hungary, Poland, Romania, Turkey, Russia
        ["YV1"] = M("Volvo", "Volvo Cars"), ["YV4"] = M("Volvo", "Volvo Cars"), ["YS3"] = M("Saab", "Saab Automobile"),
        ["LYV"] = M("Volvo", "Volvo Cars (China)"), ["TMB"] = M("Škoda", "Škoda Auto"), ["TMA"] = M("Hyundai", "Hyundai Motor Manufacturing Czech"),
        ["TMK"] = M("Kia", "Kia Czech"), ["U5Y"] = M("Kia", "Kia Slovakia"), ["U6Y"] = M("Kia", "Kia Slovakia"),
        ["TRU"] = M("Audi", "Audi Hungaria"), ["TSM"] = M("Suzuki", "Magyar Suzuki"), ["SUU"] = M("Solaris", "Solaris Bus"),
        ["NMT"] = M("Toyota", "Toyota Motor Manufacturing Turkey"), ["NM0"] = M("Ford", "Ford Otosan"), ["NLH"] = M("Hyundai", "Hyundai Assan"),
        ["NM4"] = M("Tofaş", "Tofaş (Fiat)"), ["XTA"] = M("Lada", "AvtoVAZ"), ["X7L"] = M("Renault", "Renault Russia"),
        ["Z94"] = M("Hyundai", "Hyundai Motor Manufacturing Rus"), ["XW8"] = M("Volkswagen", "Volkswagen Group Rus"),
        // Japan
        ["JTD"] = M("Toyota", "Toyota Motor Corporation"), ["JTE"] = M("Toyota", "Toyota Motor Corporation"),
        ["JTN"] = M("Toyota", "Toyota Motor Corporation"), ["JTM"] = M("Toyota", "Toyota Motor Corporation"),
        ["JTK"] = M("Toyota", "Toyota Motor Corporation"), ["JTF"] = M("Toyota", "Toyota Motor Corporation"),
        ["JTJ"] = M("Lexus", "Toyota Motor Corporation"), ["JTH"] = M("Lexus", "Toyota Motor Corporation"),
        ["JT6"] = M("Lexus", "Toyota Motor Corporation"), ["JT8"] = M("Lexus", "Toyota Motor Corporation"),
        ["JHM"] = M("Honda", "Honda Motor Co."), ["JHL"] = M("Honda", "Honda Motor Co."), ["JHG"] = M("Honda", "Honda Motor Co."),
        ["JH4"] = M("Acura", "Honda Motor Co."), ["JN1"] = M("Nissan", "Nissan Motor Co."), ["JN8"] = M("Nissan", "Nissan Motor Co."),
        ["JNK"] = M("Infiniti", "Nissan Motor Co."), ["JNR"] = M("Infiniti", "Nissan Motor Co."), ["JM1"] = M("Mazda", "Mazda Motor Corporation"),
        ["JM3"] = M("Mazda", "Mazda Motor Corporation"), ["JMZ"] = M("Mazda", "Mazda Motor Corporation (Europe)"),
        ["JF1"] = M("Subaru", "Subaru Corporation"), ["JF2"] = M("Subaru", "Subaru Corporation"),
        ["JA3"] = M("Mitsubishi", "Mitsubishi Motors"), ["JA4"] = M("Mitsubishi", "Mitsubishi Motors"), ["JMB"] = M("Mitsubishi", "Mitsubishi Motors (Europe)"),
        ["JMY"] = M("Mitsubishi", "Mitsubishi Motors"), ["JS1"] = M("Suzuki", "Suzuki Motor Corporation"), ["JS2"] = M("Suzuki", "Suzuki Motor Corporation"),
        ["JS3"] = M("Suzuki", "Suzuki Motor Corporation"), ["JSA"] = M("Suzuki", "Suzuki Motor Corporation"), ["JDA"] = M("Daihatsu", "Daihatsu Motor Co."),
        ["JAL"] = M("Isuzu", "Isuzu Motors"), ["JAA"] = M("Isuzu", "Isuzu Motors"), ["JYA"] = M("Yamaha", "Yamaha Motor"), ["JKA"] = M("Kawasaki", "Kawasaki"),
        // South Korea
        ["KMH"] = M("Hyundai", "Hyundai Motor Company"), ["KM8"] = M("Hyundai", "Hyundai Motor Company"), ["KMF"] = M("Hyundai", "Hyundai Motor Company"),
        ["KMT"] = M("Genesis", "Hyundai Motor Company"), ["KNA"] = M("Kia", "Kia Corporation"), ["KND"] = M("Kia", "Kia Corporation"),
        ["KNM"] = M("Renault Korea", "Renault Korea Motors"), ["KPT"] = M("SsangYong / KGM", "KG Mobility"), ["KL1"] = M("Chevrolet", "GM Korea"),
        ["KLA"] = M("Daewoo / Chevrolet", "GM Korea"), ["KLY"] = M("Daewoo / Chevrolet", "GM Korea"),
        // China
        ["LRW"] = M("Tesla", "Tesla Shanghai"), ["LSV"] = M("Volkswagen", "SAIC Volkswagen"), ["LFV"] = M("Volkswagen", "FAW-Volkswagen"),
        ["LBV"] = M("BMW", "BMW Brilliance"), ["LE4"] = M("Mercedes-Benz", "Beijing Benz"), ["LVS"] = M("Ford", "Changan Ford"),
        ["LVG"] = M("Toyota", "GAC Toyota"), ["LHG"] = M("Honda", "GAC Honda"), ["LGB"] = M("Nissan", "Dongfeng Nissan"),
        ["LGX"] = M("BYD", "BYD Auto"), ["LC0"] = M("BYD", "BYD Auto"), ["L6T"] = M("Geely", "Geely Automobile"), ["LB3"] = M("Geely", "Geely Automobile"),
        ["LVV"] = M("Chery", "Chery Automobile"), ["LNB"] = M("BAIC", "BAIC Motor"), ["LJD"] = M("Dongfeng Yueda Kia", "Dongfeng Yueda Kia"),
        ["LGW"] = M("Great Wall / Haval", "Great Wall Motor"), ["LSG"] = M("Chevrolet / Buick", "SAIC-GM"), ["LSJ"] = M("MG", "SAIC Motor"),
        ["LDC"] = M("Peugeot / Citroën", "Dongfeng Peugeot Citroën"), ["LPS"] = M("Polestar", "Polestar"), ["LUR"] = M("Chery Jaguar Land Rover", "Chery JLR"),
        ["LZW"] = M("Wuling", "SAIC-GM-Wuling"), ["LS5"] = M("Changan", "Changan Automobile"), ["LMG"] = M("GAC", "GAC Motor"),
        ["HJR"] = M("Jetour", "Chery Jetour"), ["LNN"] = M("Chery", "Chery Automobile"), ["LYM"] = M("NIO", "NIO"), ["LGJ"] = M("Dongfeng", "Dongfeng Fengshen"),
        // India
        ["MA1"] = M("Mahindra", "Mahindra & Mahindra"), ["MAT"] = M("Tata", "Tata Motors"), ["MA3"] = M("Maruti Suzuki", "Maruti Suzuki India"),
        ["MAL"] = M("Hyundai", "Hyundai Motor India"), ["MZB"] = M("Kia", "Kia India"), ["MBH"] = M("Suzuki", "Suzuki (India)"),
        ["MA6"] = M("Chevrolet", "GM India"), ["MAJ"] = M("Ford", "Ford India"), ["MBJ"] = M("Toyota", "Toyota Kirloskar Motor"),
        ["MAK"] = M("Honda", "Honda Cars India"), ["MEE"] = M("Renault", "Renault India"), ["MD2"] = M("Bajaj", "Bajaj Auto"),
        ["ME4"] = M("Honda", "Honda Motorcycle & Scooter India"), ["MC2"] = M("Eicher / Royal Enfield", "Eicher Motors"),
        // North America (vPIC usually knows these; kept for offline cases)
        ["5YJ"] = M("Tesla", "Tesla, Inc."), ["7SA"] = M("Tesla", "Tesla, Inc."), ["7G2"] = M("Tesla", "Tesla, Inc."),
        ["1FA"] = M("Ford", "Ford Motor Company"), ["1FM"] = M("Ford", "Ford Motor Company"), ["1FT"] = M("Ford", "Ford Motor Company"),
        ["1LN"] = M("Lincoln", "Ford Motor Company"), ["1G1"] = M("Chevrolet", "General Motors"), ["1GC"] = M("Chevrolet", "General Motors"),
        ["1GN"] = M("Chevrolet", "General Motors"), ["1GT"] = M("GMC", "General Motors"), ["1GY"] = M("Cadillac", "General Motors"),
        ["1C4"] = M("Jeep / Chrysler", "Stellantis"), ["1C3"] = M("Chrysler", "Stellantis"), ["1C6"] = M("Ram", "Stellantis"),
        ["1J4"] = M("Jeep", "Stellantis"), ["2C3"] = M("Chrysler / Dodge", "Stellantis Canada"), ["2T1"] = M("Toyota", "Toyota Canada"),
        ["2T3"] = M("Toyota", "Toyota Canada"), ["2HG"] = M("Honda", "Honda Canada"), ["2HK"] = M("Honda", "Honda Canada"),
        ["4T1"] = M("Toyota", "Toyota USA"), ["4T3"] = M("Toyota", "Toyota USA"), ["5TD"] = M("Toyota", "Toyota USA"), ["5TF"] = M("Toyota", "Toyota USA"),
        ["19X"] = M("Honda", "Honda USA"), ["1HG"] = M("Honda", "Honda USA"), ["5FN"] = M("Honda", "Honda USA"), ["5J6"] = M("Honda", "Honda USA"),
        ["1N4"] = M("Nissan", "Nissan USA"), ["5N1"] = M("Nissan", "Nissan USA"), ["4S4"] = M("Subaru", "Subaru of Indiana"), ["4S3"] = M("Subaru", "Subaru of Indiana"),
        ["5NP"] = M("Hyundai", "Hyundai Motor Manufacturing Alabama"), ["5XY"] = M("Kia", "Kia Georgia"), ["5XX"] = M("Kia", "Kia Georgia"),
        ["4JG"] = M("Mercedes-Benz", "Mercedes-Benz U.S."), ["5UX"] = M("BMW", "BMW Manufacturing (Spartanburg)"), ["5YM"] = M("BMW M", "BMW Manufacturing"),
        ["1VW"] = M("Volkswagen", "Volkswagen Chattanooga"), ["3VW"] = M("Volkswagen", "Volkswagen de México"), ["3N1"] = M("Nissan", "Nissan Mexicana"),
        ["3FA"] = M("Ford", "Ford Mexico"), ["3MZ"] = M("Mazda", "Mazda de México"), ["3KP"] = M("Kia", "Kia Mexico"),
    };

    private static readonly Dictionary<string, (string? Make, string? Manufacturer)> Wmi2 = new(StringComparer.Ordinal)
    {
        ["JT"] = M("Toyota", "Toyota Motor Corporation"), ["JH"] = M("Honda", "Honda Motor Co."), ["JN"] = M("Nissan", "Nissan Motor Co."),
        ["JM"] = M("Mazda", "Mazda Motor Corporation"), ["JF"] = M("Subaru", "Subaru Corporation"), ["JS"] = M("Suzuki", "Suzuki Motor Corporation"),
        ["JA"] = M("Mitsubishi / Isuzu", "Mitsubishi Motors / Isuzu Motors"), ["KM"] = M("Hyundai", "Hyundai Motor Company"), ["KN"] = M("Kia", "Kia Corporation"),
        ["WD"] = M("Mercedes-Benz", "Mercedes-Benz AG"), ["W1"] = M("Mercedes-Benz", "Mercedes-Benz AG"), ["WB"] = M("BMW", "BMW AG"),
        ["WV"] = M("Volkswagen", "Volkswagen AG"), ["WA"] = M("Audi", "Audi AG"), ["WP"] = M("Porsche", "Porsche AG"),
        ["ZF"] = M("Fiat", "Stellantis"), ["SA"] = M("Jaguar / Land Rover", "Jaguar Land Rover"),
        ["YV"] = M("Volvo", "Volvo Cars"), ["TM"] = M("Škoda", "Škoda Auto"), ["1G"] = M("General Motors", "General Motors"),
        ["1F"] = M("Ford", "Ford Motor Company"), ["1C"] = M("Chrysler / Jeep / Dodge", "Stellantis"), ["4T"] = M("Toyota", "Toyota USA"),
        ["5T"] = M("Toyota", "Toyota USA"),
    };
}
