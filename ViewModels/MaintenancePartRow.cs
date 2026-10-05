using System.Globalization;
using CarScanner.Services.Maintenance;
using Microsoft.Maui.Controls.Shapes;

namespace CarScanner.ViewModels;

/// <summary>One service item row on the Service page.</summary>
public sealed class MaintenancePartRow
{
    public PartId Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;

    /// <summary>Icon as a geometry for Path.Data (a plain string is not converted by a binding).</summary>
    public Geometry? IconGeometry =>
        string.IsNullOrWhiteSpace(Icon) ? null : (Geometry?)new PathGeometryConverter().ConvertFromInvariantString(Icon);
    public string ValueText { get; init; } = string.Empty;
    public Color ValueColor { get; init; } = MaintenanceText.UnknownColor;
    public double Fraction { get; init; }
    public string SubText { get; init; } = string.Empty;

    // Compact row: "3 200 km left" and a status pill (good / soon / overdue / set).
    public string DueText { get; init; } = string.Empty;
    public string PillText { get; init; } = string.Empty;
    public Color PillColor { get; init; } = MaintenanceText.UnknownColor;
    public Color PillBackground { get; init; } = Color.FromArgb("#EEF0F2");
}

/// <summary>Texts and colors shared by the Service page and the item page.</summary>
public static class MaintenanceText
{
    public static readonly Color OkColor = Color.FromArgb("#1F8A4C");
    public static readonly Color SoonColor = Color.FromArgb("#C98A1A");
    public static readonly Color OverdueColor = Color.FromArgb("#C0392B");
    public static readonly Color UnknownColor = Color.FromArgb("#9AA1A9");

    public static Color ColorOf(PartState state) => state switch
    {
        PartState.Ok => OkColor,
        PartState.Soon => SoonColor,
        PartState.Overdue => OverdueColor,
        _ => UnknownColor
    };

    // Distances are stored in km; miles are shown when Settings → Units is Imperial.
    public const double KmPerMile = 1.609344;
    public static bool Miles => Preferences.Default.Get("units", "Metric") == "Imperial";
    public static string DistanceUnit => Miles ? "mi" : "km";
    public static double ToDisplay(double km) => Miles ? km / KmPerMile : km;
    public static double FromDisplay(double value) => Miles ? value * KmPerMile : value;

    public static string Km(double km) =>
        Math.Round(ToDisplay(km)).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ') + " " + DistanceUnit;

    /// <summary>An interval in the shown unit, rounded (15 000 km → 9 300 mi).</summary>
    public static double IntervalDisplay(double km) =>
        Miles ? Math.Round(km / KmPerMile / 100d) * 100d : km;

    public static string IntervalKm(double km) =>
        IntervalDisplay(km).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ') + " " + DistanceUnit;

    public static string Months(int months, bool english)
    {
        if (months % 12 == 0)
        {
            var years = months / 12;
            return english ? $"{years} {(years == 1 ? "year" : "years")}" : $"{years} წელი";
        }

        return english ? $"{months} months" : $"{months} თვე";
    }

    public static string IntervalText(PartInterval interval, bool english)
    {
        var parts = new List<string>();
        if (interval.Km > 0)
            parts.Add(IntervalKm(interval.Km));
        if (interval.Months > 0)
            parts.Add(Months(interval.Months, english));
        return string.Join(" / ", parts);
    }

    private static string DaysText(int days, bool english)
    {
        if (days >= 60)
            return english ? $"{days / 30} months" : $"{days / 30} თვე";
        return english ? $"{days} days" : $"{days} დღე";
    }

    public static string PillText(PartState state, bool english) => state switch
    {
        PartState.Ok => english ? "Good" : "კარგად",
        PartState.Soon => english ? "Soon" : "მალე",
        PartState.Overdue => english ? "Overdue" : "ვადა გავიდა",
        _ => english ? "Set" : "მიუთითე"
    };

    public static Color PillBackground(PartState state) => state switch
    {
        PartState.Ok => Color.FromArgb("#E6F4EC"),
        PartState.Soon => Color.FromArgb("#FBF0D9"),
        PartState.Overdue => Color.FromArgb("#FBE5E1"),
        _ => Color.FromArgb("#EEF0F2")
    };

    /// <summary>"3 200 km left" / "in 4 months" / the car's own % — one short line.</summary>
    public static string DueText(PartStatus status, bool english) => status.State switch
    {
        PartState.Unknown => english ? "Enter the last change" : "მიუთითე ბოლო შეცვლა",
        PartState.Overdue when status.KmLeft is { } over && over < 0 => english ? $"{Km(-over)} over" : $"{Km(-over)}-ით გადაცილებულია",
        PartState.Overdue => english ? "Due now" : "უკვე დროა",
        _ when status.CarPercent is { } percent => english ? $"{percent:0}% · from the car" : $"{percent:0}% · მანქანიდან",
        _ when status.KmLeft is { } km => english ? $"in {Km(km)}" : $"{Km(km)}-ში",
        _ when status.DaysLeft is { } days => english ? $"in {DaysText(days, true)}" : $"{DaysText(days, false)}-ში",
        _ => string.Empty
    };

    public static string ValueText(PartStatus status, bool english) => status.State switch
    {
        PartState.Unknown => english ? "Set" : "მიუთითე",
        PartState.Overdue => english ? "Overdue" : "ვადა გავიდა",
        _ when status.CarPercent is { } percent => $"{percent:0}%",
        _ when status.KmLeft is { } km => Km(km),
        _ when status.DaysLeft is { } days => DaysText(days, english),
        _ => "—"
    };

    public static string Ago(DateTime local, bool english)
    {
        var days = (int)Math.Floor((DateTime.Now.Date - local.Date).TotalDays);
        if (days <= 0)
            return english ? "today" : "დღეს";
        if (days < 60)
            return english ? $"{days} days ago" : $"{days} დღის წინ";
        var months = days / 30;
        if (months < 24)
            return english ? $"{months} months ago" : $"{months} თვის წინ";
        var years = months / 12;
        var rest = months % 12;
        return english
            ? $"{years} years {rest} months ago"
            : rest > 0 ? $"{years} წლის და {rest} თვის წინ" : $"{years} წლის წინ";
    }

    public static string LastText(PartStatus status, bool english)
    {
        var bits = new List<string>();
        if (status.LastKm is { } km)
            bits.Add(Km(km));
        if (status.LastDate is { } date)
            bits.Add($"{date:MM.yyyy} · {Ago(date, english)}");
        return bits.Count == 0 ? string.Empty : (english ? "Last: " : "ბოლოს: ") + string.Join(" · ", bits);
    }

    public static string SubText(PartStatus status, PartInterval interval, bool english) => status.State switch
    {
        _ when status.FromCar => (english
            ? (status.CarPercent is not null ? "● Oil life from the car" : "● Distance to service from the car")
            : (status.CarPercent is not null ? "● ზეთის რესურსი მანქანიდან" : "● სერვისამდე მანძილი მანქანიდან"))
            + (LastText(status, english) is { Length: > 0 } last ? " · " + last : string.Empty),
        PartState.Unknown when status.LastDate is null && status.LastKm is null =>
            english ? $"When was it changed? Tap · {IntervalText(interval, english)}" : $"როდის შეიცვალა? დააჭირე · {IntervalText(interval, english)}",
        PartState.Unknown =>
            english ? "Enter the current odometer to count distance" : "მანძილის დასათვლელად ჩაწერე მიმდინარე გარბენი",
        _ => LastText(status, english)
    };
}
