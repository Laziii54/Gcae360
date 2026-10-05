using System.Text;
using CarScanner.Models;
using CarScanner.Services.OBD;
using HONDA = CarScanner.Services.HondaHybridBattery;
using IPACE = CarScanner.Services.JaguarIPaceBattery;
using MG = CarScanner.Services.MgZsEvBattery;

namespace CarScanner.Services;

/// <summary>Honda hybrid, Jaguar I-PACE and MG ZS EV (read-only). Taycan shares the e-tron reader.</summary>
public sealed partial class HybridHealthService
{
    // ------------------------------------------------------------ Honda CR-V / Civic hybrid 2023+

    public async Task<HvPackReading?> ReadHondaPackAsync(StringBuilder log, CancellationToken token)
    {
        if (!_obd.IsCanProtocol)
            return null;

        HvPackReading? reading = null;
        PackSampler? sampler = null;

        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            var firstCells = HONDA.DecodeCells(await AskAsync(session, HONDA.Cells, log, token).ConfigureAwait(false));
            if (firstCells.Length == 0)
                return;

            var status = HONDA.DecodeStatus(await AskAsync(session, HONDA.Status, log, token).ConfigureAwait(false));
            var temperatures = HONDA.DecodeTemperatures(await AskAsync(session, HONDA.Temperatures, log, token).ConfigureAwait(false));

            var (averaged, _) = await AverageAsync(
                new HvPackSample(firstCells, null, null),
                async () => HONDA.DecodeCells(await AskAsync(session, HONDA.Cells, null, token).ConfigureAwait(false)) is { Length: > 0 } next
                    ? new HvPackSample(next, null, null)
                    : null,
                extra: 2).ConfigureAwait(false);

            log.AppendLine($"Honda BMS (29-bit 18DA01F1): {averaged.Length} უჯრედი");
            reading = new HvPackReading(
                HvPackKind.HondaHybrid, "18DA01F1 22 2028 / 202A / 202C", averaged, temperatures, Array.Empty<int>(),
                status?.SocPercent, status?.CurrentA, status?.PackVoltage, null, null, status?.CapacityAh);

            var expected = averaged.Length;
            var iteration = 0;
            sampler = async (activeSession, activeToken) =>
            {
                var cells = HONDA.DecodeCells(await AskAsync(activeSession, HONDA.Cells, null, activeToken).ConfigureAwait(false));
                if (cells.Length != expected)
                    return null;
                double? current = null;
                if (iteration++ % 2 == 0)
                    current = HONDA.DecodeStatus(await AskAsync(activeSession, HONDA.Status, null, activeToken).ConfigureAwait(false))?.CurrentA;
                return new HvPackSample(cells, null, current);
            };
        }, token).ConfigureAwait(false);

        _sampler = sampler;
        _lastPackKind = reading?.Kind;
        return reading;
    }

    // ------------------------------------------------------------ Jaguar I-PACE

    public async Task<HvPackReading?> ReadIPacePackAsync(StringBuilder log, CancellationToken token)
    {
        if (!_obd.IsCanProtocol)
            return null;

        HvPackReading? reading = null;
        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            var high = IPACE.DecodeCell(await AskAsync(session, IPACE.MaximumCell, log, token).ConfigureAwait(false));
            var low = IPACE.DecodeCell(await AskAsync(session, IPACE.MinimumCell, log, token).ConfigureAwait(false));
            var soh = IPACE.DecodeSoh(await AskAsync(session, IPACE.Health, log, token).ConfigureAwait(false));
            if (soh is null && high is null && low is null)
                return;

            var soc = IPACE.DecodeSoc(await AskAsync(session, IPACE.Soc, log, token).ConfigureAwait(false));
            var voltage = IPACE.DecodeVoltage(await AskAsync(session, IPACE.Voltage, log, token).ConfigureAwait(false));
            var temperature = IPACE.DecodeTemperature(await AskAsync(session, IPACE.Temperature, log, token).ConfigureAwait(false));

            (double Max, double Min)? range = high is { } h && low is { } l && h >= l ? (h, l) : null;
            log.AppendLine($"I-PACE BMS: SOH {(soh.HasValue ? $"{soh:0.#}%" : "—")} · მაქს/მინ უჯრედი {(range is { } r ? $"{r.Max:0.000}/{r.Min:0.000} V" : "—")}");
            reading = new HvPackReading(
                HvPackKind.JaguarIPace, "7E4 22 4903 / 4904 / 4918", Array.Empty<double>(),
                temperature is { } t ? new[] { t } : Array.Empty<double>(), Array.Empty<int>(),
                soc, null, voltage, soh, null, null, range);
        }, token).ConfigureAwait(false);

        _sampler = null;
        _lastPackKind = reading?.Kind;
        return reading;
    }

    // ------------------------------------------------------------ MG ZS EV

    public async Task<HvPackReading?> ReadMgZsEvPackAsync(StringBuilder log, CancellationToken token)
    {
        if (!_obd.IsCanProtocol)
            return null;

        HvPackReading? reading = null;
        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            var soc = MG.DecodeSoc(await AskAsync(session, MG.Soc, log, token).ConfigureAwait(false));
            var voltage = MG.DecodeVoltage(await AskAsync(session, MG.Voltage, log, token).ConfigureAwait(false));
            var soh = MG.DecodeSoh(await AskAsync(session, MG.Health, log, token).ConfigureAwait(false));

            var modules = new List<MG.ModuleReading>();
            foreach (var request in MG.Modules)
                if (MG.DecodeModule(await AskAsync(session, request, log, token).ConfigureAwait(false)) is { } module)
                    modules.Add(module);

            if (soc is null && soh is null && modules.Count == 0)
                return;

            (double Max, double Min)? range = modules.Count >= 2
                ? (modules.Max(m => m.MaxVolts), modules.Min(m => m.MinVolts))
                : null;
            var temperatures = modules.Count > 0
                ? new[] { modules.Min(m => m.MinTemperature), modules.Max(m => m.MaxTemperature) }
                : Array.Empty<double>();

            log.AppendLine($"MG ZS EV BMS: SOC {(soc.HasValue ? $"{soc:0.#}%" : "—")} · SOH {(soh.HasValue ? $"{soh:0.#}%" : "—")} · {modules.Count} მოდული");
            reading = new HvPackReading(
                HvPackKind.MgZsEv, "781 22 B046 / 7E5 22 B009…", Array.Empty<double>(), temperatures, Array.Empty<int>(),
                soc, null, voltage, soh, null, null, range,
                Note: modules.Count > 0 ? $"უჯრედების სხვაობა {modules.Count} მოდულის მაქს/მინ ძაბვიდან" : null);
        }, token).ConfigureAwait(false);

        _sampler = null;
        _lastPackKind = reading?.Kind;
        return reading;
    }
}
