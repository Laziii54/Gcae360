using CarScanner.Models;

namespace CarScanner.Services;

/// <summary>
/// What the vehicle card says. Only a manufacturer / model / year that the current session really obtained is shown as the
/// vehicle; a brand the person picked earlier is a preference, not a connected vehicle, and never appears here.
/// </summary>
public static class VehicleIdentityText
{
    public sealed record Lines(string Name, string Vin, string Trim, string Fuel);

    public static string Unknown(bool english) => english ? "Unknown" : "უცნობია";

    /// <summary>Texts of the vehicle card while no adapter is connected.</summary>
    public static Lines Disconnected(bool english) => english
        ? new Lines("Vehicle unknown", "VIN unknown", "Trim unavailable", "Fuel unavailable")
        : new Lines("მანქანა უცნობია", "VIN უცნობია", "კომპლექტაცია მიუწვდომელია", "საწვავი მიუწვდომელია");

    /// <summary>Make, model and year of the identified vehicle, or the "unknown" text when none of them was obtained.</summary>
    public static string Name(VehicleInfo? vehicle, bool english)
    {
        var parts = new[] { vehicle?.Manufacturer, vehicle?.Model, vehicle?.Year }
            .Where(value => !string.IsNullOrWhiteSpace(value) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase));
        var name = string.Join(" ", parts);
        return name.Length > 0 ? name : Disconnected(english).Name;
    }

    public static bool HasValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
}
