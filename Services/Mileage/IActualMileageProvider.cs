using CarScanner.Models;

namespace CarScanner.Services.Mileage;

public interface IActualMileageProvider
{
    string Name { get; }
    Task<IReadOnlyList<MileageEvidence>> ReadAllAsync(
        VehicleInfo vehicle,
        CancellationToken cancellationToken = default);
}
