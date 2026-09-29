using CarScanner.Models;

namespace CarScanner.Services.Mileage;

public interface IVerifiedMileageReader
{
    string SourceKind { get; }

    Task<MileageEvidence?> TryReadAsync(
        VehicleInfo vehicle,
        VerifiedUdsMileageProfile profile,
        CancellationToken cancellationToken = default);
}
