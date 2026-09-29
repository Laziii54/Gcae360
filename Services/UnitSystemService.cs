using System.Globalization;
using CarScanner.Models;
using Microsoft.Maui.Storage;

namespace CarScanner.Services;

public enum UnitSystem
{
    Metric,
    Imperial
}

public readonly record struct UnitValue(double Value, string Unit);

/// <summary>
/// The one presentation-unit source for the app. OBD values remain in their
/// standard metric units internally; conversion happens only when a value is
/// presented so diagnostic calculations are never affected by Settings.
/// </summary>
public sealed class UnitSystemService
{
    private const string PreferenceKey = "units";
    private UnitSystem _current;

    public UnitSystemService()
    {
        _current = Parse(Preferences.Default.Get(PreferenceKey, "Metric"));
    }

    public UnitSystem Current => _current;
    public bool IsImperial => _current == UnitSystem.Imperial;
    public string DisplayName => IsImperial ? "Imperial" : "Metric";
    public string SpeedUnit => IsImperial ? "mph" : "km/h";
    public string DistanceUnit => IsImperial ? "mi" : "km";
    public string TemperatureUnit => IsImperial ? "°F" : "°C";
    public string PressureUnit => IsImperial ? "PSI" : "kPa";
    public string PerformanceTestLabel => IsImperial ? "0–60 mph" : "0–100 km/h";
    public double PerformanceTargetKph => IsImperial ? 60d * 1.609344d : 100d;

    public event EventHandler? UnitSystemChanged;

    public void Set(string? value) => Set(Parse(value));

    public void Set(UnitSystem system)
    {
        Preferences.Default.Set(PreferenceKey, system == UnitSystem.Imperial ? "Imperial" : "Metric");
        if (_current == system)
            return;

        _current = system;
        UnitSystemChanged?.Invoke(this, EventArgs.Empty);
    }

    public UnitValue Convert(double metricValue, string? metricUnit)
    {
        var unit = (metricUnit ?? string.Empty).Trim();
        if (!IsImperial)
            return new UnitValue(metricValue, unit);

        return unit switch
        {
            "km/h" => new UnitValue(metricValue * 0.621371192d, "mph"),
            "km" => new UnitValue(metricValue * 0.621371192d, "mi"),
            "m" => new UnitValue(metricValue * 3.280839895d, "ft"),
            "°C" => new UnitValue(metricValue * 9d / 5d + 32d, "°F"),
            "kPa" => new UnitValue(metricValue * 0.145037738d, "PSI"),
            "Pa" => new UnitValue(metricValue * 0.000145037738d, "PSI"),
            "bar" => new UnitValue(metricValue * 14.5037738d, "PSI"),
            "L/100km" => metricValue > 0d ? new UnitValue(235.214583d / metricValue, "MPG") : new UnitValue(metricValue, "MPG"),
            "L/h" => new UnitValue(metricValue * 0.264172052d, "gal/h"),
            "L" => new UnitValue(metricValue * 0.264172052d, "gal"),
            _ => new UnitValue(metricValue, unit)
        };
    }

    public SensorValue Convert(SensorValue metric)
    {
        if (!double.TryParse(metric.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return metric;

        var converted = Convert(value, metric.Unit);
        return new SensorValue
        {
            Pid = metric.Pid,
            Name = metric.Name,
            Unit = converted.Unit,
            Value = converted.Value.ToString("0.##", CultureInfo.InvariantCulture),
            CanonicalValue = metric.CanonicalValue.Length == 0 ? metric.Value : metric.CanonicalValue,
            Category = metric.Category
        };
    }

    public ObdTelemetrySample Convert(ObdTelemetrySample metric)
    {
        var converted = Convert(metric.Value, metric.Unit);
        return new ObdTelemetrySample
        {
            Timestamp = metric.Timestamp,
            Pid = metric.Pid,
            Name = metric.Name,
            Unit = converted.Unit,
            Value = converted.Value
        };
    }

    public string Format(double metricValue, string metricUnit, string format = "0.##")
    {
        var converted = Convert(metricValue, metricUnit);
        return $"{converted.Value.ToString(format, CultureInfo.InvariantCulture)} {converted.Unit}".Trim();
    }

    public string FormatSpeed(double kph, string format = "0") => Format(kph, "km/h", format);
    public string FormatDistance(double km, string format = "0.0") => Format(km, "km", format);
    public string FormatTemperature(double celsius, string format = "0") => Format(celsius, "°C", format);
    public string FormatPressure(double kpa, string format = "0.0") => Format(kpa, "kPa", format);
    public string FormatFuelEconomy(double litersPer100Km, string format = "0.0") => Format(litersPer100Km, "L/100km", format);
    public double DistanceToKilometers(double displayedDistance) => IsImperial ? displayedDistance / 0.621371192d : displayedDistance;

    private static UnitSystem Parse(string? value) => string.Equals(value, "Imperial", StringComparison.OrdinalIgnoreCase)
        ? UnitSystem.Imperial
        : UnitSystem.Metric;
}
