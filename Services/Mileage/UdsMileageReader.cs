using CarScanner.Models;
using CarScanner.Services.UDS;

namespace CarScanner.Services.Mileage;

public sealed class UdsMileageReader : IVerifiedMileageReader
{
    private readonly UdsProtocol _uds;

    public string SourceKind => "UDS";

    public UdsMileageReader(UdsProtocol uds)
    {
        _uds = uds;
    }

    public async Task<MileageEvidence?> TryReadAsync(
        VehicleInfo vehicle,
        VerifiedUdsMileageProfile profile,
        CancellationToken cancellationToken = default)
    {
        var request =
            new byte[]
            {
                0x22,
                (byte)(profile.Did >> 8),
                (byte)(profile.Did & 0xFF)
            };

        var response =
            await _uds.SendIsoTpAsync(
                request,
                profile.TxCanId,
                profile.RxCanId,
                profile.Extended29Bit,
                cancellationToken);

        if (response is null)
            return null;

        if (!UdsMileageResponseAdapter.TryGetPayload(
                response,
                out var payload))
        {
            return null;
        }

        if (!UdsMileageDecoder.TryDecode(
                payload,
                profile,
                out var mileage,
                out var error))
        {
            return null;
        }

        return new MileageEvidence(
            Source: profile.Source,
            Ecu: profile.Ecu,
            ValueKm: mileage,
            Confidence: profile.Confidence,
            Details:
                $"Verified UDS DID 0x{profile.Did:X4}. " +
                $"{profile.Notes}");
    }
}
