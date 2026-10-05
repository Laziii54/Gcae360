namespace CarScanner.Services.Maintenance;

/// <summary>Service items tracked one by one on the Service page.</summary>
public enum PartId
{
    Oil,
    AirFilter,
    CabinFilter,
    SparkPlugs,
    BrakeFluid,
    Coolant,
    Transmission,
    Brakes,
    FuelFilter
}

public enum PartState
{
    Unknown,
    Ok,
    Soon,
    Overdue
}

public enum PowertrainKind
{
    Petrol,
    Diesel,
    Hybrid,
    Electric
}

/// <summary>0 means "not counted" (e.g. BMW brake fluid is by time only).</summary>
public sealed record PartInterval(int Km, int Months)
{
    public bool IsEmpty => Km <= 0 && Months <= 0;
}

public sealed record PartDefinition(PartId Id, string NameKa, string NameEn, string Icon, bool InspectionOnly);

/// <summary>Remaining distance/time for one item.</summary>
public sealed record PartStatus(
    PartState State,
    double? KmLeft,
    int? DaysLeft,
    double Fraction,
    DateTime? LastDate,
    double? LastKm,
    double? CarPercent = null,
    bool FromCar = false);

/// <summary>
/// Typical manufacturer intervals (normal driving conditions). They differ by
/// engine, so every value can be changed by the user on the item page.
/// </summary>
public static class MaintenanceSchedule
{
    public const string Toyota = "toyota";
    public const string Mercedes = "mercedes";
    public const string Hyundai = "hyundai";
    public const string Bmw = "bmw";
    public const string Vag = "vag";
    public const string Ford = "ford";
    public const string Gm = "gm";
    public const string Honda = "honda";
    public const string Nissan = "nissan";
    public const string Mazda = "mazda";
    public const string Subaru = "subaru";
    public const string Volvo = "volvo";
    public const string Renault = "renault";
    public const string Psa = "psa";
    public const string Fca = "fca";
    public const string Tesla = "tesla";
    public const string Other = "other";

    /// <summary>US manufacturers publish intervals in miles.</summary>
    private static int Mi(int miles) => (int)Math.Round(miles * 1.609344);

    private const string IconOil = "M4,14 L8,10 L16,10 L19,13 L19,17 L4,17 Z M10,10 L10,7 L14,7 M8,7 L16,7";
    private const string IconFilter = "M4,6 L20,6 M4,12 L20,12 M4,18 L20,18 M7,4 L7,20 M12,4 L12,20 M17,4 L17,20";
    private const string IconPlug = "M10,3 L14,3 L14,7 L10,7 Z M9,7 L15,7 L15,13 L9,13 Z M12,13 L12,19 M10,19 L14,19";
    private const string IconDrop = "M12,4 C12,4 6,11 6,15 C6,18 9,20 12,20 C15,20 18,18 18,15 C18,11 12,4 12,4 Z";
    private const string IconThermo = "M10,4 L14,4 L14,14 A4,4 0 1 1 10,14 Z M12,10 L12,17";
    private const string IconGear = "M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 M12,2 L12,5 M12,19 L12,22 M2,12 L5,12 M19,12 L22,12";
    private const string IconBrake = "M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8";
    private const string IconFuel = "M5,20 L5,5 L13,5 L13,20 Z M5,11 L13,11 M13,8 L17,11 L17,17 C17,19 19,19 19,17 L19,9";

    public static IReadOnlyList<PartDefinition> AllParts { get; } = new[]
    {
        new PartDefinition(PartId.Oil, "ძრავის ზეთი და ფილტრი", "Engine oil and filter", IconOil, false),
        new PartDefinition(PartId.BrakeFluid, "სამუხრუჭე სითხე", "Brake fluid", IconDrop, false),
        new PartDefinition(PartId.AirFilter, "ჰაერის ფილტრი", "Air filter", IconFilter, false),
        new PartDefinition(PartId.CabinFilter, "სალონის ფილტრი", "Cabin filter", IconFilter, false),
        new PartDefinition(PartId.SparkPlugs, "სანთლები", "Spark plugs", IconPlug, false),
        new PartDefinition(PartId.FuelFilter, "საწვავის ფილტრი", "Fuel filter", IconFuel, false),
        new PartDefinition(PartId.Brakes, "ხუნდები (შემოწმება)", "Brake pads (check)", IconBrake, true),
        new PartDefinition(PartId.Coolant, "გაგრილების სითხე", "Coolant", IconThermo, false),
        new PartDefinition(PartId.Transmission, "კოლოფის ზეთი", "Transmission fluid", IconGear, false)
    };

    public static PartDefinition Get(PartId id) => AllParts.First(part => part.Id == id);

    /// <summary>Items that make sense for this powertrain.</summary>
    public static IReadOnlyList<PartDefinition> PartsFor(PowertrainKind kind) => AllParts
        .Where(part => part.Id switch
        {
            PartId.Oil or PartId.AirFilter => kind != PowertrainKind.Electric,
            PartId.SparkPlugs => kind is PowertrainKind.Petrol or PowertrainKind.Hybrid,
            PartId.FuelFilter => kind == PowertrainKind.Diesel,
            PartId.Transmission => kind != PowertrainKind.Electric,
            _ => true
        })
        .ToArray();

    public static PowertrainKind PowertrainFrom(string? fuelType)
    {
        var text = (fuelType ?? string.Empty).ToLowerInvariant();
        if (text.Contains("hybrid") || text.Contains("ჰიბრიდ"))
            return PowertrainKind.Hybrid;
        if (text.Contains("electric") || text.Contains("ელექტრ"))
            return PowertrainKind.Electric;
        if (text.Contains("diesel") || text.Contains("დიზელ"))
            return PowertrainKind.Diesel;
        return PowertrainKind.Petrol;
    }

    public static string BrandGroup(string? make)
    {
        var value = new string((make ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        if (value.Length == 0)
            return Other;
        if (value.Contains("toyota") || value.Contains("lexus"))
            return Toyota;
        if (value.Contains("mercedes") || value.Contains("benz"))
            return Mercedes;
        if (value.Contains("hyundai") || value.Contains("kia") || value.Contains("genesis"))
            return Hyundai;
        if (value.Contains("bmw") || value.Contains("mini"))
            return Bmw;
        if (value.Contains("volkswagen") || value == "vw" || value.Contains("audi") || value.Contains("skoda") ||
            value.Contains("seat") || value.Contains("cupra"))
            return Vag;
        if (value.Contains("ford") || value.Contains("lincoln"))
            return Ford;
        if (value.Contains("chevrolet") || value.Contains("gmc") || value.Contains("cadillac") || value.Contains("buick") ||
            value == "generalmotors" || value == "gm")
            return Gm;
        if (value.Contains("honda") || value.Contains("acura"))
            return Honda;
        if (value.Contains("nissan") || value.Contains("infiniti") || value.Contains("datsun"))
            return Nissan;
        if (value.Contains("mazda"))
            return Mazda;
        if (value.Contains("subaru"))
            return Subaru;
        if (value.Contains("volvo") || value.Contains("polestar"))
            return Volvo;
        if (value.Contains("renault") || value.Contains("dacia"))
            return Renault;
        if (value.Contains("peugeot") || value.Contains("citroen") || value.Contains("citroën") || value == "ds" ||
            value.Contains("dsautomobiles") || value.Contains("opel") || value.Contains("vauxhall"))
            return Psa;
        if (value.Contains("fiat") || value.Contains("alfaromeo") || value.Contains("lancia") || value.Contains("jeep") ||
            value.Contains("chrysler") || value.Contains("dodge") || value == "ram" || value.Contains("abarth"))
            return Fca;
        if (value.Contains("tesla"))
            return Tesla;
        return Other;
    }

    public static string BrandGroupName(string group) => group switch
    {
        Toyota => "Toyota/Lexus",
        Mercedes => "Mercedes-Benz",
        Hyundai => "Hyundai/Kia",
        Bmw => "BMW/MINI",
        Vag => "VW/Audi",
        Ford => "Ford",
        Gm => "GM (Chevrolet/GMC/Cadillac)",
        Honda => "Honda/Acura",
        Nissan => "Nissan/Infiniti",
        Mazda => "Mazda",
        Subaru => "Subaru",
        Volvo => "Volvo",
        Renault => "Renault/Dacia",
        Psa => "Peugeot/Citroën/Opel",
        Fca => "Fiat/Jeep/Chrysler",
        Tesla => "Tesla",
        _ => string.Empty
    };

    public static PartInterval DefaultInterval(string group, PartId id) => (group, id) switch
    {
        (Toyota, PartId.Oil) => new(15000, 12),
        (Toyota, PartId.AirFilter) => new(30000, 24),
        (Toyota, PartId.CabinFilter) => new(15000, 12),
        (Toyota, PartId.SparkPlugs) => new(120000, 96),
        (Toyota, PartId.BrakeFluid) => new(30000, 24),
        (Toyota, PartId.Coolant) => new(160000, 120),
        (Toyota, PartId.Transmission) => new(60000, 48),
        (Toyota, PartId.Brakes) => new(15000, 12),

        (Mercedes, PartId.Oil) => new(20000, 12),
        (Mercedes, PartId.AirFilter) => new(80000, 60),
        (Mercedes, PartId.CabinFilter) => new(40000, 24),
        (Mercedes, PartId.SparkPlugs) => new(80000, 60),
        (Mercedes, PartId.BrakeFluid) => new(40000, 24),
        (Mercedes, PartId.Coolant) => new(200000, 180),
        (Mercedes, PartId.Transmission) => new(60000, 72),
        (Mercedes, PartId.Brakes) => new(20000, 12),

        (Hyundai, PartId.Oil) => new(15000, 12),
        (Hyundai, PartId.AirFilter) => new(30000, 24),
        (Hyundai, PartId.CabinFilter) => new(15000, 12),
        (Hyundai, PartId.SparkPlugs) => new(75000, 60),
        (Hyundai, PartId.BrakeFluid) => new(30000, 24),
        (Hyundai, PartId.Coolant) => new(200000, 120),
        (Hyundai, PartId.Transmission) => new(60000, 48),
        (Hyundai, PartId.Brakes) => new(15000, 12),

        (Bmw, PartId.Oil) => new(15000, 12),
        (Bmw, PartId.AirFilter) => new(60000, 48),
        (Bmw, PartId.CabinFilter) => new(30000, 24),
        (Bmw, PartId.SparkPlugs) => new(60000, 48),
        (Bmw, PartId.BrakeFluid) => new(0, 24),
        (Bmw, PartId.Coolant) => new(150000, 120),
        (Bmw, PartId.Transmission) => new(80000, 72),
        (Bmw, PartId.Brakes) => new(15000, 12),

        (Vag, PartId.Oil) => new(15000, 12),
        (Vag, PartId.AirFilter) => new(60000, 48),
        (Vag, PartId.CabinFilter) => new(30000, 24),
        (Vag, PartId.SparkPlugs) => new(60000, 72),
        (Vag, PartId.BrakeFluid) => new(0, 24),
        (Vag, PartId.Coolant) => new(200000, 120),
        (Vag, PartId.Transmission) => new(60000, 48),
        (Vag, PartId.Brakes) => new(15000, 12),

        // Typical owner's-manual values for normal driving; the exact numbers
        // depend on the engine and market, so every value stays editable.
        (Ford, PartId.Oil) => new(Mi(10000), 12),
        (Ford, PartId.AirFilter) => new(Mi(30000), 48),
        (Ford, PartId.CabinFilter) => new(Mi(20000), 24),
        (Ford, PartId.SparkPlugs) => new(Mi(100000), 120),
        (Ford, PartId.BrakeFluid) => new(0, 36),
        (Ford, PartId.Coolant) => new(Mi(100000), 72),
        (Ford, PartId.Transmission) => new(Mi(150000), 120),
        (Ford, PartId.Brakes) => new(Mi(10000), 12),

        (Gm, PartId.Oil) => new(Mi(7500), 12),
        (Gm, PartId.AirFilter) => new(Mi(45000), 48),
        (Gm, PartId.CabinFilter) => new(Mi(22500), 24),
        (Gm, PartId.SparkPlugs) => new(Mi(97500), 120),
        (Gm, PartId.BrakeFluid) => new(0, 60),
        (Gm, PartId.Coolant) => new(Mi(150000), 60),
        (Gm, PartId.Transmission) => new(Mi(97500), 96),
        (Gm, PartId.Brakes) => new(Mi(7500), 12),

        (Honda, PartId.Oil) => new(Mi(7500), 12),
        (Honda, PartId.AirFilter) => new(Mi(30000), 36),
        (Honda, PartId.CabinFilter) => new(Mi(15000), 12),
        (Honda, PartId.SparkPlugs) => new(Mi(105000), 120),
        (Honda, PartId.BrakeFluid) => new(0, 36),
        (Honda, PartId.Coolant) => new(Mi(120000), 120),
        (Honda, PartId.Transmission) => new(Mi(30000), 36),
        (Honda, PartId.Brakes) => new(Mi(7500), 12),

        (Nissan, PartId.Oil) => new(15000, 12),
        (Nissan, PartId.AirFilter) => new(Mi(30000), 36),
        (Nissan, PartId.CabinFilter) => new(Mi(15000), 12),
        (Nissan, PartId.SparkPlugs) => new(Mi(105000), 96),
        (Nissan, PartId.BrakeFluid) => new(0, 24),
        (Nissan, PartId.Coolant) => new(Mi(105000), 84),
        (Nissan, PartId.Transmission) => new(Mi(60000), 48),
        (Nissan, PartId.Brakes) => new(Mi(7500), 12),

        (Mazda, PartId.Oil) => new(15000, 12),
        (Mazda, PartId.AirFilter) => new(Mi(30000), 36),
        (Mazda, PartId.CabinFilter) => new(20000, 12),
        (Mazda, PartId.SparkPlugs) => new(Mi(75000), 96),
        (Mazda, PartId.BrakeFluid) => new(0, 24),
        (Mazda, PartId.Coolant) => new(Mi(120000), 120),
        (Mazda, PartId.Transmission) => new(Mi(90000), 96),
        (Mazda, PartId.Brakes) => new(Mi(7500), 12),

        (Subaru, PartId.Oil) => new(Mi(6000), 6),
        (Subaru, PartId.AirFilter) => new(Mi(30000), 30),
        (Subaru, PartId.CabinFilter) => new(Mi(15000), 12),
        (Subaru, PartId.SparkPlugs) => new(Mi(60000), 72),
        (Subaru, PartId.BrakeFluid) => new(Mi(30000), 30),
        (Subaru, PartId.Coolant) => new(Mi(137500), 132),
        (Subaru, PartId.Transmission) => new(Mi(60000), 72),
        (Subaru, PartId.Brakes) => new(Mi(6000), 6),

        (Volvo, PartId.Oil) => new(15000, 12),
        (Volvo, PartId.AirFilter) => new(60000, 48),
        (Volvo, PartId.CabinFilter) => new(30000, 12),
        (Volvo, PartId.SparkPlugs) => new(Mi(60000), 72),
        (Volvo, PartId.BrakeFluid) => new(0, 24),
        (Volvo, PartId.Coolant) => new(200000, 120),
        (Volvo, PartId.Transmission) => new(Mi(120000), 120),
        (Volvo, PartId.Brakes) => new(15000, 12),

        (Renault, PartId.Oil) => new(15000, 12),
        (Renault, PartId.AirFilter) => new(60000, 48),
        (Renault, PartId.CabinFilter) => new(15000, 12),
        (Renault, PartId.SparkPlugs) => new(60000, 48),
        (Renault, PartId.BrakeFluid) => new(0, 24),
        (Renault, PartId.Coolant) => new(120000, 48),
        (Renault, PartId.Transmission) => new(120000, 96),
        (Renault, PartId.Brakes) => new(15000, 12),

        (Psa, PartId.Oil) => new(15000, 12),
        (Psa, PartId.AirFilter) => new(60000, 48),
        (Psa, PartId.CabinFilter) => new(20000, 12),
        (Psa, PartId.SparkPlugs) => new(60000, 48),
        (Psa, PartId.BrakeFluid) => new(0, 24),
        (Psa, PartId.Coolant) => new(240000, 120),
        (Psa, PartId.Transmission) => new(120000, 96),
        (Psa, PartId.Brakes) => new(20000, 12),

        (Fca, PartId.Oil) => new(15000, 12),
        (Fca, PartId.AirFilter) => new(30000, 24),
        (Fca, PartId.CabinFilter) => new(15000, 12),
        (Fca, PartId.SparkPlugs) => new(60000, 48),
        (Fca, PartId.BrakeFluid) => new(0, 24),
        (Fca, PartId.Coolant) => new(Mi(150000), 120),
        (Fca, PartId.Transmission) => new(120000, 96),
        (Fca, PartId.Brakes) => new(15000, 12),

        // Tesla: no oil, filters or plugs; brake fluid health check every 4 years.
        (Tesla, PartId.CabinFilter) => new(0, 24),
        (Tesla, PartId.BrakeFluid) => new(0, 48),
        (Tesla, PartId.Coolant) => new(0, 96),
        (Tesla, PartId.Brakes) => new(20000, 12),

        (_, PartId.FuelFilter) => new(60000, 48),

        // Other brands: common conservative values.
        (_, PartId.Oil) => new(10000, 12),
        (_, PartId.AirFilter) => new(30000, 24),
        (_, PartId.CabinFilter) => new(15000, 12),
        (_, PartId.SparkPlugs) => new(60000, 48),
        (_, PartId.BrakeFluid) => new(40000, 24),
        (_, PartId.Coolant) => new(100000, 60),
        (_, PartId.Transmission) => new(60000, 48),
        (_, PartId.Brakes) => new(15000, 12),
        _ => new(0, 0)
    };

    /// <summary>Share of the interval left at which an item turns yellow.</summary>
    public const double SoonFraction = 0.2;

    /// <summary>Oil life at or below which the car's value turns yellow.</summary>
    public const double SoonOilLifePercent = 15;

    /// <summary>Distance to service at or below which the car's value turns yellow.</summary>
    public const double SoonServiceKm = 1500;

    /// <summary>
    /// Status from the car's own value: oil life in % (GM, Ford …) or
    /// distance to the next service (Mercedes …).
    /// </summary>
    public static PartStatus FromCar(double? oilLifePercent, double? serviceDistanceKm, DateTime? lastDate, double? lastKm)
    {
        if (oilLifePercent is { } percent)
        {
            var state = percent <= 0 ? PartState.Overdue : percent <= SoonOilLifePercent ? PartState.Soon : PartState.Ok;
            return new PartStatus(state, serviceDistanceKm, null, Math.Clamp(percent / 100d, 0, 1), lastDate, lastKm, percent, true);
        }

        if (serviceDistanceKm is { } km)
        {
            var state = km <= 0 ? PartState.Overdue : km <= SoonServiceKm ? PartState.Soon : PartState.Ok;
            // Fraction against a typical 20 000 km service interval (only for the bar).
            return new PartStatus(state, km, null, Math.Clamp(km / 20000d, 0, 1), lastDate, lastKm, null, true);
        }

        return new PartStatus(PartState.Unknown, null, null, 0, lastDate, lastKm);
    }

    public static PartStatus Calculate(
        PartInterval interval,
        DateTime? lastDate,
        double? lastKm,
        double? currentKm,
        DateTime now)
    {
        if (lastDate is null && lastKm is null)
            return new PartStatus(PartState.Unknown, null, null, 0, null, null);

        double? kmLeft = null;
        double? kmFraction = null;
        if (interval.Km > 0 && lastKm is { } fromKm && currentKm is { } nowKm)
        {
            kmLeft = interval.Km - Math.Max(0, nowKm - fromKm);
            kmFraction = kmLeft.Value / interval.Km;
        }

        int? daysLeft = null;
        double? dayFraction = null;
        if (interval.Months > 0 && lastDate is { } fromDate)
        {
            var due = fromDate.Date.AddMonths(interval.Months);
            var total = Math.Max(1, (due - fromDate.Date).TotalDays);
            daysLeft = (int)Math.Floor((due - now.Date).TotalDays);
            dayFraction = daysLeft.Value / total;
        }

        if (kmFraction is null && dayFraction is null)
            return new PartStatus(PartState.Unknown, null, null, 0, lastDate, lastKm);

        var fraction = Math.Min(kmFraction ?? double.MaxValue, dayFraction ?? double.MaxValue);
        var state = kmLeft < 0 || daysLeft < 0
            ? PartState.Overdue
            : fraction <= SoonFraction
                ? PartState.Soon
                : PartState.Ok;

        return new PartStatus(state, kmLeft, daysLeft, Math.Clamp(fraction, 0, 1), lastDate, lastKm);
    }
}
