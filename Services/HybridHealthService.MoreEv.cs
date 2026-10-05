using System.Globalization;
using System.Text;
using CarScanner.Models;
using FORD = CarScanner.Services.FordHybridBattery;
using I3 = CarScanner.Services.BmwI3Battery;
using EGOLF = CarScanner.Services.VwEGolfBattery;

namespace CarScanner.Services;

/// <summary>BMW i3, Ford Fusion / C-Max hybrids, VW e-Golf: direct, read-only reading.</summary>
public sealed partial class HybridHealthService
{
    // ------------------------------------------------------------ BMW i3

    public async Task<HvPackReading?> ReadI3PackAsync(VehicleInfo vehicle, StringBuilder log, CancellationToken token)
    {
        if (!_obd.IsCanProtocol)
            return null;

        int? year = int.TryParse(vehicle.Year, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        HvPackReading? reading = null;

        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            var soh = I3.DecodeSoh(await AskAsync(session, I3.Health, log, token).ConfigureAwait(false));
            var range = I3.DecodeCellRange(await AskAsync(session, I3.CellRange, log, token).ConfigureAwait(false));
            if (soh is null && range is null)
                return;

            var soc = I3.DecodeSoc(await AskAsync(session, I3.Soc, log, token).ConfigureAwait(false));
            var voltage = I3.DecodeVoltage(await AskAsync(session, I3.Voltage, log, token).ConfigureAwait(false));
            var current = I3.DecodeCurrent(await AskAsync(session, I3.Current, log, token).ConfigureAwait(false));
            var temperatures = I3.DecodeTemperatures(await AskAsync(session, I3.Temperatures, log, token).ConfigureAwait(false));
            var capacity = I3.DecodeAverageCapacity(await AskAsync(session, I3.CellStatistics, log, token).ConfigureAwait(false));
            var nominal = I3.NominalAh(year);

            reading = new HvPackReading(
                HvPackKind.BmwI3, "6F1/07 22 6335 / DDBF", Array.Empty<double>(), temperatures, Array.Empty<int>(),
                soc, current, voltage, soh, null, capacity, range,
                RangeLoadTest: range is not null,
                Note: capacity is { } ah && nominal is { } newAh
                    ? $"ახალი ბატარეა: {newAh} Ah · ახლა ≈ {Math.Round(ah / newAh * 100)}%"
                    : null);
        }, token).ConfigureAwait(false);

        _sampler = reading?.CellRange is not null ? RangeSampler(I3.CellRange, I3.DecodeCellRange) : null;
        _lastPackKind = reading?.Kind;
        return reading;
    }

    // ------------------------------------------------------------ Ford hybrids

    public async Task<HvPackReading?> ReadFordPackAsync(bool machE, StringBuilder log, CancellationToken token)
    {
        if (!_obd.IsCanProtocol)
            return null;

        HvPackReading? reading = null;
        double? lowest = null;

        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            var spread = FORD.DecodeMillivolts(await AskAsync(session, FORD.VoltageSpread, log, token).ConfigureAwait(false), 2);
            lowest = FORD.DecodeMillivolts(await AskAsync(session, FORD.MinimumModule, log, token).ConfigureAwait(false), 5);
            var soc = FORD.DecodeSoc(await AskAsync(session, FORD.Soc, log, token).ConfigureAwait(false));
            if (spread is null && lowest is null && soc is null)
                return;

            var temperatures = FORD.DecodeTemperatureRange(await AskAsync(session, FORD.TemperatureRange, log, token).ConfigureAwait(false));
            if (temperatures.Length == 0 && FORD.DecodeTemperature(await AskAsync(session, FORD.Temperature, log, token).ConfigureAwait(false)) is { } single)
                temperatures = new[] { single };
            var voltage = FORD.DecodeVoltage(await AskAsync(session, FORD.Voltage, log, token).ConfigureAwait(false));
            var current = machE
                ? FORD.DecodeMachECurrent(await AskAsync(session, FORD.MachECurrent, log, token).ConfigureAwait(false))
                : FORD.DecodeCurrent(await AskAsync(session, FORD.Current, log, token).ConfigureAwait(false));
            var age = FORD.DecodeAgeYears(await AskAsync(session, FORD.Age, log, token).ConfigureAwait(false));
            // SOH is confirmed on the Mach-E only.
            var soh = machE ? FORD.DecodeSoh(await AskAsync(session, FORD.Health, log, token).ConfigureAwait(false)) : null;

            (double Max, double Min)? range = lowest is { } min && spread is { } gap ? (Math.Round(min + gap, 4), min) : null;
            reading = new HvPackReading(
                HvPackKind.FordHybrid, "7E4 22 483F / 4840", Array.Empty<double>(), temperatures, Array.Empty<int>(),
                soc, current, voltage, soh, null, null, range,
                RangeLoadTest: range is not null,
                Note: age is { } years ? $"ბატარეის ასაკი მოდულის მიხედვით ≈ {years:0.#} წელი" : null);
        }, token).ConfigureAwait(false);

        _sampler = reading?.CellRange is not null && lowest is { } floor
            ? RangeSampler(FORD.VoltageSpread, data => FORD.DecodeMillivolts(data, 2) is { } gap ? (Math.Round(floor + gap, 4), floor) : null)
            : null;
        _lastPackKind = reading?.Kind;
        return reading;
    }

    // ------------------------------------------------------------ VW e-Golf

    public async Task<HvPackReading?> ReadEGolfPackAsync(StringBuilder log, CancellationToken token)
    {
        if (!_obd.IsCanProtocol)
            return null;

        HvPackReading? reading = null;
        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            var high = EGOLF.DecodeCell(await AskAsync(session, EGOLF.MaximumCell, log, token).ConfigureAwait(false));
            var low = EGOLF.DecodeCell(await AskAsync(session, EGOLF.MinimumCell, log, token).ConfigureAwait(false));
            var soc = EGOLF.DecodeSoc(await AskAsync(session, EGOLF.Soc, log, token).ConfigureAwait(false));
            if (high is null && low is null && soc is null)
                return;

            var voltage = EGOLF.DecodeVoltage(await AskAsync(session, EGOLF.Voltage, log, token).ConfigureAwait(false));
            var current = EGOLF.DecodeCurrent(await AskAsync(session, EGOLF.Current, log, token).ConfigureAwait(false));
            (double Max, double Min)? range = high is { } h && low is { } l && h.Volts >= l.Volts ? (h.Volts, l.Volts) : null;

            reading = new HvPackReading(
                HvPackKind.VwEGolf, "7E5 22 1E33 / 1E34", Array.Empty<double>(), Array.Empty<double>(), Array.Empty<int>(),
                soc, current, voltage, null, null, null, range,
                Note: high is { } top && low is { } bottom ? $"ყველაზე მაღალი: უჯრედი {top.Cell} · ყველაზე დაბალი: უჯრედი {bottom.Cell}" : null);
        }, token).ConfigureAwait(false);

        _sampler = null;
        _lastPackKind = reading?.Kind;
        return reading;
    }

    // ------------------------------------------------------------ Audi e-tron

    public async Task<HvPackReading?> ReadEtronPackAsync(StringBuilder log, IProgress<string>? progress, CancellationToken token, HvPackKind kind = HvPackKind.AudiEtron)
    {
        if (!_obd.IsCanProtocol)
            return null;

        HvPackReading? reading = null;
        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            var high = AudiEtronBattery.DecodeCell(await AskAsync(session, AudiEtronBattery.MaximumCell, log, token).ConfigureAwait(false));
            var low = AudiEtronBattery.DecodeCell(await AskAsync(session, AudiEtronBattery.MinimumCell, log, token).ConfigureAwait(false));
            var soc = AudiEtronBattery.DecodeSoc(await AskAsync(session, AudiEtronBattery.Soc, log, token).ConfigureAwait(false));
            if (high is null && low is null && soc is null)
                return;

            var soh = AudiEtronBattery.DecodeSoh(await AskAsync(session, AudiEtronBattery.Health, log, token).ConfigureAwait(false));
            // Awaited at once: one request at a time on the adapter.
            var temperatures = await new[] { AudiEtronBattery.MinimumTemperature, AudiEtronBattery.MaximumTemperature }
                .ToAsyncList(request => AskAsync(session, request, log, token), AudiEtronBattery.DecodeTemperature).ConfigureAwait(false);

            // Module cells, then a cross-check with the BMS's own highest / lowest cell.
            var cells = new List<double>();
            for (var module = 0; module < AudiEtronBattery.ModuleCount; module++)
            {
                if (module % 6 == 0)
                    progress?.Report($"მოდულების კითხვა… {module}/{AudiEtronBattery.ModuleCount}");
                var part = AudiEtronBattery.DecodeModule(await AskAsync(session, AudiEtronBattery.Module(module), module == 0 ? log : null, token).ConfigureAwait(false));
                if (part.Length == 0)
                    break;
                cells.AddRange(part);
            }

            (double Max, double Min)? range = high is { } h && low is { } l && h >= l ? (h, l) : null;
            // The module layout is confirmed on two recordings only: use the cells
            // only when they agree with the BMS's own highest / lowest cell.
            if (cells.Count > 0 && (range is not { } known ||
                Math.Abs(cells.Max() - known.Max) > 0.015 || Math.Abs(cells.Min() - known.Min) > 0.015))
            {
                log.AppendLine(range is { } shown
                    ? $"{(kind == HvPackKind.PorscheTaycan ? "Taycan" : "e-tron")}: მოდულების უჯრედები არ ემთხვევა BMS-ის მაქს/მინ-ს ({shown.Max:0.000}/{shown.Min:0.000} V) — არ გამოიყენება."
                    : $"{(kind == HvPackKind.PorscheTaycan ? "Taycan" : "e-tron")}: BMS-ის მაქს/მინ უჯრედი არ დაბრუნდა — მოდულების უჯრედები ვერ გადამოწმდა და არ გამოიყენება.");
                cells.Clear();
            }
            if (cells.Count is > 0 and < 10)
                cells.Clear();

            log.AppendLine($"{(kind == HvPackKind.PorscheTaycan ? "Taycan" : "e-tron")} BMS: {(cells.Count > 0 ? $"{cells.Count} უჯრედი" : range is null ? "უჯრედები არ არის" : "მხოლოდ მაქს/მინ უჯრედი")} · SOH {(soh.HasValue ? $"{soh:0.#}%" : "—")}");
            reading = new HvPackReading(
                kind, "7E5 22 1850… / 1E33 / 1E34", cells.ToArray(), temperatures, Array.Empty<int>(),
                soc, null, null, soh, null, null, range);
        }, token).ConfigureAwait(false);

        _sampler = null;
        _lastPackKind = reading?.Kind;
        return reading;
    }

    // ------------------------------------------------------------ Haval Jolion HEV

    public async Task<HvPackReading?> ReadHavalPackAsync(StringBuilder log, CancellationToken token)
    {
        if (!_obd.IsCanProtocol)
            return null;

        HvPackReading? reading = null;
        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            var soh = HavalHevBattery.DecodeSoh(await AskAsync(session, HavalHevBattery.Health, log, token).ConfigureAwait(false));
            var high = HavalHevBattery.DecodeCell(await AskAsync(session, HavalHevBattery.MaximumCell, log, token).ConfigureAwait(false));
            var low = HavalHevBattery.DecodeCell(await AskAsync(session, HavalHevBattery.MinimumCell, log, token).ConfigureAwait(false));
            if (soh is null && high is null && low is null)
                return;

            var soc = HavalHevBattery.DecodeSoc(await AskAsync(session, HavalHevBattery.Soc, log, token).ConfigureAwait(false));
            var voltage = HavalHevBattery.DecodeVoltage(await AskAsync(session, HavalHevBattery.Voltage, log, token).ConfigureAwait(false));
            var current = HavalHevBattery.DecodeCurrent(await AskAsync(session, HavalHevBattery.Current, log, token).ConfigureAwait(false));
            var highCell = HavalHevBattery.DecodePosition(await AskAsync(session, HavalHevBattery.MaximumCellPosition, log, token).ConfigureAwait(false));
            var lowCell = HavalHevBattery.DecodePosition(await AskAsync(session, HavalHevBattery.MinimumCellPosition, log, token).ConfigureAwait(false));

            (double Max, double Min)? range = high is { } h && low is { } l && h >= l ? (h, l) : null;
            reading = new HvPackReading(
                HvPackKind.HavalHev, "78B 22 E0F5 / E008 / E009", Array.Empty<double>(), Array.Empty<double>(), Array.Empty<int>(),
                soc, current, voltage, soh, null, null, range,
                Note: highCell is { } top && lowCell is { } bottom ? $"ყველაზე მაღალი: უჯრედი {top} · ყველაზე დაბალი: უჯრედი {bottom}" : null);
        }, token).ConfigureAwait(false);

        _sampler = null;
        _lastPackKind = reading?.Kind;
        return reading;
    }

    /// <summary>Load-test sampler for packs that report the highest / lowest cell in one answer.</summary>
    private static PackSampler RangeSampler(HvRequest request, Func<byte[]?, (double Max, double Min)?> decode) =>
        async (session, token) =>
            decode(await AskAsync(session, request, null, token).ConfigureAwait(false)) is { } range
                ? new HvPackSample(new[] { range.Max, range.Min }, null, null)
                : null;
}

internal static class HvAsyncExtensions
{
    /// <summary>Asks one request after the other (one session) and keeps the decoded values.</summary>
    public static async Task<double[]> ToAsyncList(
        this IEnumerable<HvRequest> requests,
        Func<HvRequest, Task<byte[]?>> ask,
        Func<byte[]?, double?> decode)
    {
        var values = new List<double>();
        foreach (var request in requests)
            if (decode(await ask(request).ConfigureAwait(false)) is { } value)
                values.Add(value);
        return values.ToArray();
    }
}
