using CarScanner.Models;

namespace CarScanner.Services.Mileage;

public sealed class VerifiedCanOdometerProvider : IActualMileageProvider
{
    private readonly ICanFrameSource? _frames;

    private static readonly VerifiedCanOdometerProfile[] Profiles =
    {
        new(
            Brand: "Toyota",
            VehicleProfile: "Prius 2017",
            Ecu: "UI_SETTING",
            CanId: 1553,
            Extended29Bit: false,
            OdometerStartBit: 43,
            OdometerBitLength: 20,
            UnitStartBit: 26,
            UnitBitLength: 2,
            KmUnitValue: 1,
            MilesUnitValue: 2,
            Confidence: 0.80,
            Source: "Toyota Prius 2017 DBC",
            SourceUrl: "https://gitea.interbiznw.com/rusefi/opendbc/src/commit/61cca0a81adf155ef9de43958426c2ba0a0a73eb/toyota_prius_2017_pt_generated.dbc",
            Notes: "Verified DBC signal definition; unit code is normalized before display."),

        new(
            Brand: "Lexus",
            VehicleProfile: "CT200h 2018",
            Ecu: "UI_SETTING",
            CanId: 1553,
            Extended29Bit: false,
            OdometerStartBit: 43,
            OdometerBitLength: 20,
            UnitStartBit: 26,
            UnitBitLength: 2,
            KmUnitValue: 1,
            MilesUnitValue: 2,
            Confidence: 0.80,
            Source: "Lexus CT200h 2018 DBC",
            SourceUrl: "https://gitea.interbiznw.com/rusefi/opendbc/src/commit/3afceadb847346feb30d96506f482f536e36281d/lexus_ct200h_2018_pt_generated.dbc",
            Notes: "Verified DBC signal definition; unit code is normalized before display.")
    };

    public string Name => "Verified CAN odometer profiles";

    public VerifiedCanOdometerProvider(ICanFrameSource? frames = null)
    {
        _frames = frames;
    }

    public async Task<IReadOnlyList<MileageEvidence>> ReadAllAsync(
        VehicleInfo vehicle,
        CancellationToken cancellationToken = default)
    {
        if (_frames is null)
            return Array.Empty<MileageEvidence>();

        var model = vehicle.Model ?? string.Empty;
        var year = vehicle.Year ?? string.Empty;

        var matches =
            Profiles.Where(p =>
                string.Equals(
                    p.Brand,
                    vehicle.Manufacturer,
                    StringComparison.OrdinalIgnoreCase) &&
                (
                    string.Equals(
                        p.VehicleProfile,
                        $"{model} {year}".Trim(),
                        StringComparison.OrdinalIgnoreCase) ||
                    (
                        p.Brand.Equals("Toyota", StringComparison.OrdinalIgnoreCase) &&
                        model.Contains("Prius", StringComparison.OrdinalIgnoreCase) &&
                        year.StartsWith("2017", StringComparison.OrdinalIgnoreCase) &&
                        p.VehicleProfile == "Prius 2017"
                    ) ||
                    (
                        p.Brand.Equals("Lexus", StringComparison.OrdinalIgnoreCase) &&
                        model.Contains("CT200h", StringComparison.OrdinalIgnoreCase) &&
                        year.StartsWith("2018", StringComparison.OrdinalIgnoreCase) &&
                        p.VehicleProfile == "CT200h 2018"
                    )
                ))
            .ToArray();

        if (matches.Length == 0)
            return Array.Empty<MileageEvidence>();

        var result = new List<MileageEvidence>();

        foreach (var profile in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frame =
                await _frames.ReadFrameAsync(
                    profile.CanId,
                    profile.Extended29Bit,
                    TimeSpan.FromSeconds(2),
                    cancellationToken);

            if (!frame.HasValue)
                continue;

            if (!VerifiedCanOdometerDecoder.TryDecode(
                    frame.Value.Data.Span,
                    profile,
                    out var kilometers,
                    out var decodeDetails))
                continue;

            result.Add(
                new MileageEvidence(
                    Source: profile.Source,
                    Ecu: profile.Ecu,
                    ValueKm: kilometers,
                    Confidence: profile.Confidence,
                    Details: decodeDetails + " " + profile.Notes));
        }

        return result;
    }
}
