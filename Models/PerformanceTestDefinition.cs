using CarScanner.Services;

namespace CarScanner.Models;

/// <summary>
/// A speed interval measured exclusively from standard OBD-II PID 01 0D.
/// Speeds are stored in km/h so the measuring algorithm never depends on UI
/// unit settings; presentation is converted only on screen.
/// </summary>
public sealed record PerformanceTestDefinition(
    string Id,
    string DisplayName,
    double StartSpeedKph,
    double TargetSpeedKph,
    IReadOnlyList<int> SplitTargetsKph)
{
    public bool RequiresStationaryStart => StartSpeedKph <= 0.01d;
    public string StartInstruction => RequiresStationaryStart
        ? "გააჩერე მანქანა 0–3 km/h-ზე, შემდეგ დაიწყე აჩქარება."
        : $"ჯერ ჩამოდი {StartSpeedKph:0} km/h-მდე ან ქვემოთ, შემდეგ დაიწყე აჩქარება.";
}

public static class PerformanceTests
{
    public static IReadOnlyList<PerformanceTestDefinition> Standard { get; } =
    [
        new("0-50", "0–50 km/h", 0, 50, [10, 20, 30, 40]),
        new("0-100", "0–100 km/h", 0, 100, [20, 50, 80]),
        new("0-200", "0–200 km/h", 0, 200, [50, 100, 150]),
        new("0-60mph", "0–60 mph", 0, 60 * 1.609344d, [32, 64, 80]),
        new("30-70", "30–70 km/h", 30, 70, [40, 50, 60]),
        new("60-100", "60–100 km/h", 60, 100, [70, 80, 90]),
        new("80-120", "80–120 km/h", 80, 120, [90, 100, 110]),
        new("100-200", "100–200 km/h", 100, 200, [120, 140, 160, 180])
    ];

    public static PerformanceTestDefinition Default(UnitSystemService units) => units.IsImperial
        ? Standard.First(test => test.Id == "0-60mph")
        : Standard.First(test => test.Id == "0-100");
}
