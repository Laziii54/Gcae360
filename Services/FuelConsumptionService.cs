using CarScanner.Models;
namespace CarScanner.Services;
public sealed class FuelConsumptionService
{
    private readonly LiveDataEngine _engine;
    public FuelConsumptionService(LiveDataEngine engine)=>_engine=engine;
    public static FuelRateMeasurement? ResolveFuelRate(
        double? directFuelRateLitersPerHour,
        double? mafGramsPerSecond,
        int? fuelTypeCode)
    {
        if (directFuelRateLitersPerHour is > 0 and < 3213)
            return new FuelRateMeasurement(directFuelRateLitersPerHour.Value, true);
        if (mafGramsPerSecond is not > 0)
            return null;

        // MAF is g/s and fuel density is kg/L, so density must be converted to
        // g/L before calculating L/h. This prevents a 1000x overestimate.
        var (afr, densityKgPerLiter) = (fuelTypeCode ?? 1) switch
        {
            4 => (14.5, 0.835), // diesel
            3 => (9.0, 0.789),  // ethanol
            2 => (6.5, 0.792),  // methanol
            5 => (15.5, 0.58),  // LPG
            6 => (17.2, 0.72),  // CNG, approximate density
            _ => (14.7, 0.745)  // petrol
        };
        var litersPerHour = mafGramsPerSecond.Value / afr / (densityKgPerLiter * 1000d) * 3600d;
        return litersPerHour is > 0 and < 100
            ? new FuelRateMeasurement(litersPerHour, false)
            : null;
    }

    public static double? FuelEconomyLPer100Km(double? fuelRateLitersPerHour, double speedKph) =>
        fuelRateLitersPerHour is > 0 && speedKph > 1
            ? fuelRateLitersPerHour.Value / speedKph * 100d
            : null;
}

public readonly record struct FuelRateMeasurement(double LitersPerHour, bool IsDirect);
