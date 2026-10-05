using System.Globalization;
using System.Text;
using CarScanner.Models;
using CarScanner.Services.OBD;
using T = CarScanner.Services.ToyotaHybridBattery;

namespace CarScanner.Services;

/// <summary>Toyota / Lexus: direct read of the HV battery ECU (read-only).</summary>
public sealed partial class HybridHealthService
{
    /// <summary>Tries the known Toyota layouts (ordered by model year) and reads the first that answers.</summary>
    public async Task<HvPackReading?> ReadToyotaPackAsync(VehicleInfo vehicle, StringBuilder log, CancellationToken token)
    {
        if (!_obd.IsCanProtocol)
        {
            log.AppendLine("Toyota HV: მანქანა CAN პროტოკოლზე არ არის — პირდაპირი წაკითხვა გამოტოვებულია.");
            return null;
        }

        int? year = int.TryParse(vehicle.Year, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        HvPackReading? reading = null;
        PackSampler? sampler = null;

        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            foreach (var kind in T.ProbeOrder(year))
            {
                foreach (var request in BlockRequests(kind))
                {
                    var first = Decode(kind, await AskAsync(session, request, log, token).ConfigureAwait(false));
                    if (first is null)
                        continue;

                    var (blocks, samples) = await AverageAsync(
                        first,
                        async () => Decode(kind, await AskAsync(session, request, null, token).ConfigureAwait(false)),
                        extra: 2).ConfigureAwait(false);
                    double? soc = samples.LastOrDefault(sample => sample.SocPercent.HasValue)?.SocPercent;
                    double? current = samples.LastOrDefault(sample => sample.CurrentA.HasValue)?.CurrentA;
                    double? packVoltage = null;
                    var temperatures = Array.Empty<double>();
                    var resistances = Array.Empty<int>();

                    switch (kind)
                    {
                        case HvPackKind.ToyotaGen2NiMh:
                            temperatures = T.DecodeGen2Temperatures(await AskAsync(session, T.Gen2Thermal, log, token).ConfigureAwait(false));
                            resistances = T.DecodeGen2Resistances(await AskAsync(session, T.Gen2Resistance, log, token).ConfigureAwait(false));
                            break;

                        // The extra 7E2 requests belong to the documented 7E2 layout only.
                        case HvPackKind.ToyotaGen3NiMh when request == T.Gen3Blocks:
                            temperatures = T.DecodeGen3Temperatures(await AskAsync(session, T.Gen3Thermal, log, token).ConfigureAwait(false));
                            resistances = T.DecodeGen3Resistances(await AskAsync(session, T.Gen3Resistance, log, token).ConfigureAwait(false), blocks.Length);
                            current = T.DecodeGen3Current(await AskAsync(session, T.Gen3Current, log, token).ConfigureAwait(false));
                            soc = T.DecodeGen3Soc(await AskAsync(session, T.Gen3Status, log, token).ConfigureAwait(false));
                            break;

                        case HvPackKind.ToyotaCells:
                            temperatures = T.DecodeTngaTemperatures(await AskAsync(session, T.TngaTemperatures, log, token).ConfigureAwait(false));
                            soc = T.DecodeTngaSoc(await AskAsync(session, T.TngaSoc, log, token).ConfigureAwait(false));
                            (packVoltage, current) = T.DecodeTngaPower(await AskAsync(session, T.TngaPower, log, token).ConfigureAwait(false));
                            break;
                    }

                    if (resistances.Length != blocks.Length)
                        resistances = Array.Empty<int>();

                    log.AppendLine($"{request.Label}: {blocks.Length} {(kind == HvPackKind.ToyotaCells ? "უჯრედი" : "ბლოკი")} · {samples.Count} წაკითხვა");
                    reading = new HvPackReading(kind, request.Label, blocks, temperatures, resistances, soc, current, packVoltage, null);
                    sampler = ToyotaSampler(kind, request);
                    return;
                }
            }
        }, token).ConfigureAwait(false);

        _sampler = sampler;
        _lastPackKind = reading?.Kind;
        return reading;
    }

    /// <summary>Load-test sampler: blocks (and current where it costs one request).</summary>
    private static PackSampler ToyotaSampler(HvPackKind kind, HvRequest request)
    {
        var iteration = 0;
        return async (session, token) =>
        {
            var sample = Decode(kind, await AskAsync(session, request, null, token).ConfigureAwait(false));
            if (sample is null)
                return null;

            if (iteration++ % 2 == 0)
            {
                if (kind == HvPackKind.ToyotaCells)
                    sample = sample with { CurrentA = T.DecodeTngaPower(await AskAsync(session, T.TngaPower, null, token).ConfigureAwait(false)).Current };
                else if (kind == HvPackKind.ToyotaGen3NiMh && request == T.Gen3Blocks)
                    sample = sample with { CurrentA = T.DecodeGen3Current(await AskAsync(session, T.Gen3Current, null, token).ConfigureAwait(false)) };
            }
            return sample;
        };
    }

    private static IEnumerable<HvRequest> BlockRequests(HvPackKind kind) => kind switch
    {
        HvPackKind.ToyotaGen2NiMh => new[] { T.Gen2Blocks },
        HvPackKind.ToyotaGen3NiMh => new[] { T.Gen3Blocks, T.Gen3BlocksAlternative },
        _ => new[] { T.TngaCells }
    };

    private static HvPackSample? Decode(HvPackKind kind, byte[]? data) => kind switch
    {
        HvPackKind.ToyotaGen2NiMh => T.DecodeGen2Blocks(data),
        HvPackKind.ToyotaGen3NiMh => T.DecodeGen3Blocks(data),
        _ => T.DecodeTngaCells(data)
    };
}
