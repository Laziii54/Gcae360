using System.Globalization;
using System.Text;
using CarScanner.Models;
using LEAF = CarScanner.Services.NissanLeafBattery;

namespace CarScanner.Services;

/// <summary>Nissan Leaf / e-NV200: direct read of the lithium battery controller (read-only).</summary>
public sealed partial class HybridHealthService
{
    public async Task<HvPackReading?> ReadLeafPackAsync(VehicleInfo vehicle, StringBuilder log, CancellationToken token)
    {
        if (!_obd.IsCanProtocol)
        {
            log.AppendLine("Leaf HV: მანქანა CAN პროტოკოლზე არ არის — პირდაპირი წაკითხვა გამოტოვებულია.");
            return null;
        }

        int? year = int.TryParse(vehicle.Year, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        HvPackReading? reading = null;
        var cellsTrusted = false;

        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            var soh = LEAF.DecodeSoh(await AskAsync(session, LEAF.Health, log, token).ConfigureAwait(false));
            var status = LEAF.DecodeStatus(await AskAsync(session, LEAF.Status, log, token).ConfigureAwait(false), year);
            if (soh is null && status is null)
                return; // the battery controller did not answer

            var range = LEAF.DecodeCellRange(await AskAsync(session, LEAF.CellRange, log, token).ConfigureAwait(false));
            var cells = LEAF.DecodeCells(await AskAsync(session, LEAF.Cells, log, token).ConfigureAwait(false));

            // 21 02 has no recorded answer in OBDb yet: use the cells only when they
            // match the controller's own highest / lowest cell (when it reports them).
            if (cells is not null && range is { } known && !LEAF.AgreesWithRange(cells, known))
            {
                log.AppendLine($"Leaf 21 02: უჯრედები არ ემთხვევა 21 03-ის მაქს/მინ-ს ({known.Max:0.000}/{known.Min:0.000} V) — არ გამოიყენება.");
                cells = null;
            }

            if (cells is not null)
            {
                var second = LEAF.DecodeCells(await AskAsync(session, LEAF.Cells, null, token).ConfigureAwait(false));
                if (second is not null)
                    cells = cells.Select((value, index) => Math.Round((value + second[index]) / 2, 4)).ToArray();
                cellsTrusted = range is not null;
            }

            var blocks = cells ?? Array.Empty<double>();
            if (cells is null)
                log.AppendLine(range is null
                    ? "Leaf: უჯრედების ძაბვა არ დაბრუნდა — ნაჩვენებია მხოლოდ SOH."
                    : "Leaf: ცალკეული უჯრედები არ დაბრუნდა — სხვაობა მაქს/მინ უჯრედიდანაა.");

            log.AppendLine($"Leaf LBC: SOH {(soh.HasValue ? $"{soh:0.#}%" : "—")} · {(cells is null ? "უჯრედები არ არის" : $"{cells.Length} უჯრედი")}");
            reading = new HvPackReading(
                HvPackKind.NissanLeaf,
                "79B 21 01 / 21 02 / 21 61",
                blocks,
                Array.Empty<double>(),
                Array.Empty<int>(),
                status?.SocPercent,
                null,
                status?.PackVoltage,
                soh,
                null,
                status?.CapacityAh,
                range,
                RangeLoadTest: cells is null && range is not null);
        }, token).ConfigureAwait(false);

        // The load test needs all 96 cells from one request.
        _sampler = reading is { Blocks.Length: 96 } ? LeafSampler()
            : reading is { RangeLoadTest: true } ? RangeSampler(LEAF.CellRange, LEAF.DecodeCellRange)
            : null;
        _lastPackKind = reading?.Kind;
        if (reading is not null && !cellsTrusted && reading.Blocks.Length == 96)
            log.AppendLine("Leaf: უჯრედების მნიშვნელობა 21 03-ით ვერ გადამოწმდა — მიიღე როგორც მიახლოებითი.");
        return reading;
    }

    private static PackSampler LeafSampler() => async (session, token) =>
    {
        var cells = LEAF.DecodeCells(await AskAsync(session, LEAF.Cells, null, token).ConfigureAwait(false));
        return cells is null ? null : new HvPackSample(cells, null, null);
    };
}
