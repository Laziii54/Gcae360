using CarScanner.Models;

namespace CarScanner.Services.Mileage;

public sealed class GenericObdMileageProvider : IActualMileageProvider
{
    private readonly OBDService _obd;
    public string Name => "Generic OBD-II PID 01 A6";
    public GenericObdMileageProvider(OBDService obd) => _obd = obd;

    public async Task<IReadOnlyList<MileageEvidence>> ReadAllAsync(
        VehicleInfo vehicle, CancellationToken cancellationToken = default)
    {
        try
        {
            _obd.EnsureInitialized();
            var value = await _obd.ReadStandardOdometerAsync(cancellationToken);
            if (!value.HasValue || value.Value < 0 || value.Value > 2_000_000)
                return Array.Empty<MileageEvidence>();
            return new[] { new MileageEvidence(Name, "Generic OBD-II", value.Value, 0.55,
                "ECU returned a value for optional Mode 01 PID A6. One generic source cannot verify original mileage.") };
        }
        catch { return Array.Empty<MileageEvidence>(); }
    }
}
