using CarScanner.Models;

namespace CarScanner.Services.Mileage;

public sealed class VerifiedProfileMileageProvider :
    IActualMileageProvider
{
    private readonly VerifiedMileageProfileRegistry _registry;
    private readonly IEnumerable<IVerifiedMileageReader> _readers;

    public string Name => "Verified OEM / UDS profiles";

    public VerifiedProfileMileageProvider(
        VerifiedMileageProfileRegistry registry,
        IEnumerable<IVerifiedMileageReader> readers)
    {
        _registry = registry;
        _readers = readers;
    }

    public async Task<IReadOnlyList<MileageEvidence>> ReadAllAsync(
        VehicleInfo vehicle,
        CancellationToken cancellationToken = default)
    {
        var profiles =
            _registry.FindUds(
                vehicle.Manufacturer,
                vehicle.Model,
                vehicle.Year);

        if (profiles.Count == 0)
            return Array.Empty<MileageEvidence>();

        var result = new List<MileageEvidence>();

        foreach (var profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var reader in _readers)
            {
                if (!string.Equals(
                        reader.SourceKind,
                        profile.Source,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var evidence =
                        await reader.TryReadAsync(
                            vehicle,
                            profile,
                            cancellationToken);

                    if (evidence is not null)
                        result.Add(evidence);
                }
                catch
                {
                    // An individual ECU/profile failure must not
                    // cancel other verified sources.
                }
            }
        }

        return result;
    }
}
