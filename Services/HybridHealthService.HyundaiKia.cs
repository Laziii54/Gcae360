using System.Text;
using CarScanner.Models;
using CarScanner.Services.OBD;
using HK = CarScanner.Services.HyundaiKiaBattery;

namespace CarScanner.Services;

/// <summary>Hyundai / Kia / Genesis: direct read of the battery management system (read-only).</summary>
public sealed partial class HybridHealthService
{
    public async Task<HvPackReading?> ReadHyundaiKiaPackAsync(StringBuilder log, CancellationToken token)
    {
        if (!_obd.IsCanProtocol)
        {
            log.AppendLine("Hyundai/Kia HV: მანქანა CAN პროტოკოლზე არ არის — პირდაპირი წაკითხვა გამოტოვებულია.");
            return null;
        }

        HvPackReading? reading = null;
        PackSampler? sampler = null;

        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            // Newer cars answer 22 0101, older ones 21 01.
            foreach (var generation in new[] { HK.Generation.Uds22, HK.Generation.Kwp21 })
            {
                var status = HK.DecodeStatus(await AskAsync(session, HK.Status(generation), log, token).ConfigureAwait(false));
                if (status is null)
                    continue;

                var first = await ReadCellsAsync(session, generation, log, token).ConfigureAwait(false);
                if (first is null)
                {
                    log.AppendLine("Hyundai/Kia: BMS-მა უპასუხა, მაგრამ უჯრედების ძაბვა არ დააბრუნა.");
                    continue;
                }

                var healthData = await AskAsync(session, HK.Health(generation), log, token).ConfigureAwait(false);
                var soh = HK.DecodeSoh(healthData);
                var cellRequests = HK.CellRequests(generation);

                // 98-cell EV packs carry cells 97–98 in the health block.
                var blocks = first.Blocks;
                if (blocks.Length == 96)
                    blocks = blocks.Concat(HK.DecodeExtraCells(healthData)).ToArray();

                var (averaged, samples) = await AverageAsync(
                    first with { Blocks = blocks },
                    async () =>
                    {
                        var next = await ReadCellsAsync(session, generation, null, token).ConfigureAwait(false);
                        return next is null || next.Blocks.Length > blocks.Length ? null
                            : next.Blocks.Length == blocks.Length ? next
                            : next with { Blocks = next.Blocks.Concat(blocks.Skip(next.Blocks.Length)).ToArray() };
                    },
                    extra: 1).ConfigureAwait(false);

                log.AppendLine($"Hyundai/Kia BMS ({(generation == HK.Generation.Uds22 ? "22 01xx" : "21 0x")}): {averaged.Length} უჯრედი · SOH {(soh.HasValue ? $"{soh:0.#}%" : "არ არის")}");
                reading = new HvPackReading(
                    HvPackKind.HyundaiKia,
                    HK.Status(generation).Label,
                    averaged,
                    status.Temperatures,
                    Array.Empty<int>(),
                    status.SocPercent,
                    status.CurrentA,
                    status.PackVoltage,
                    soh);
                sampler = HyundaiKiaSampler(generation, averaged.Length);
                return;
            }
        }, token).ConfigureAwait(false);

        _sampler = sampler;
        _lastPackKind = reading?.Kind;
        return reading;
    }

    /// <summary>All cell requests in order until one does not answer (EVs have more of them than hybrids).</summary>
    private static async Task<HvPackSample?> ReadCellsAsync(
        Elm327Protocol.IDtcModuleSession session,
        HK.Generation generation,
        StringBuilder? log,
        CancellationToken token)
    {
        var cells = new List<double>();
        foreach (var request in HK.CellRequests(generation))
        {
            var part = HK.DecodeCells(await AskAsync(session, request, log, token).ConfigureAwait(false));
            if (part.Length == 0)
                break;
            cells.AddRange(part);
            if (part.Length < 32)
                break; // the last, partly filled request
        }
        return cells.Count >= 10 ? new HvPackSample(cells.ToArray(), null, null) : null;
    }

    /// <summary>Load-test sampler: cells, and the current from the status block every second time.</summary>
    private static PackSampler HyundaiKiaSampler(HK.Generation generation, int cellCount)
    {
        var iteration = 0;
        // 98-cell packs: cells 97–98 are not re-read during the test.
        var expected = cellCount == 98 ? 96 : cellCount;
        return async (session, token) =>
        {
            var sample = await ReadCellsAsync(session, generation, null, token).ConfigureAwait(false);
            if (sample is null || sample.Blocks.Length != expected)
                return null;

            if (iteration++ % 2 == 0)
            {
                var status = HK.DecodeStatus(await AskAsync(session, HK.Status(generation), null, token).ConfigureAwait(false));
                sample = sample with { CurrentA = status?.CurrentA };
            }
            return sample;
        };
    }
}
