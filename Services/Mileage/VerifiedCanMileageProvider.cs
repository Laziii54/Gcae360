using CarScanner.Models;

namespace CarScanner.Services.Mileage;

public sealed class VerifiedCanMileageProvider : IActualMileageProvider
{
    private readonly VerifiedMileageProfileRegistry _registry;
    private readonly ICanFrameSource? _frames;
    public string Name => "Verified CAN mileage profiles";

    public VerifiedCanMileageProvider(VerifiedMileageProfileRegistry registry, ICanFrameSource? frames = null)
    { _registry = registry; _frames = frames; }

    public async Task<IReadOnlyList<MileageEvidence>> ReadAllAsync(
        VehicleInfo vehicle, CancellationToken cancellationToken = default)
    {
        if (_frames is null) return Array.Empty<MileageEvidence>();
        var profiles = _registry.FindCan(vehicle.Manufacturer, vehicle.Model, vehicle.Year);
        if (profiles.Count == 0) return Array.Empty<MileageEvidence>();
        var result = new List<MileageEvidence>();
        foreach (var p in profiles) {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await _frames.ReadFrameAsync(p.CanId, p.Extended29Bit, TimeSpan.FromSeconds(2), cancellationToken);
            if (!frame.HasValue) continue;
            if (!CanMileageDecoder.TryDecode(frame.Value.Data.Span, p, out var km, out _)) continue;
            result.Add(new MileageEvidence(p.Source, p.Ecu, km, p.Confidence, $"Verified CAN 0x{p.CanId:X}. {p.Notes}"));
        }
        return result;
    }
}
