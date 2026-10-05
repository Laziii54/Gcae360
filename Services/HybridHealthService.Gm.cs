using System.Globalization;
using System.Text;
using CarScanner.Models;
using GM = CarScanner.Services.GmVoltBoltBattery;

namespace CarScanner.Services;

/// <summary>Chevrolet Volt / Bolt family: direct read of the BECM (read-only).</summary>
public sealed partial class HybridHealthService
{
    public async Task<HvPackReading?> ReadGmPackAsync(
        VehicleInfo vehicle,
        bool bolt,
        StringBuilder log,
        IProgress<string>? progress,
        CancellationToken token)
    {
        if (!_obd.IsCanProtocol)
        {
            log.AppendLine("GM HV: მანქანა CAN პროტოკოლზე არ არის — პირდაპირი წაკითხვა გამოტოვებულია.");
            return null;
        }

        int? year = int.TryParse(vehicle.Year, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        HvPackReading? reading = null;

        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            var cells = new List<double>();
            var numbers = new List<int>();
            for (var index = 0; index < GM.CellCount; index++)
            {
                token.ThrowIfCancellationRequested();
                if (index % 8 == 0)
                    progress?.Report($"უჯრედების კითხვა… {index}/{GM.CellCount}");
                var value = GM.DecodeCell(await AskAsync(session, GM.CellRequest(index), index < 3 ? log : null, token).ConfigureAwait(false));
                if (value is { } volts)
                {
                    cells.Add(volts);
                    numbers.Add(index + 1);
                }
                else if (index == 2 && cells.Count == 0)
                {
                    log.AppendLine("GM: BECM (7E7) უჯრედების ძაბვას არ აბრუნებს.");
                    return; // not a Volt / Bolt BECM, or the car is off
                }
            }

            log.AppendLine($"GM BECM 7E7 22 4181…4240: {cells.Count}/{GM.CellCount} უჯრედმა უპასუხა");
            if (cells.Count < 80)
            {
                log.AppendLine("GM: ძალიან ბევრმა უჯრედმა არ უპასუხა — ჩართე მანქანა (READY) და გაიმეორე.");
                return;
            }

            progress?.Report("ტემპერატურა და დამუხტვა…");
            var temperatures = new List<double>();
            double? soc = null, packVoltage = null, current = null, capacity = null;
            if (bolt)
            {
                foreach (var request in new[] { GM.BoltMinTemperature, GM.BoltMaxTemperature })
                    if (GM.DecodeTemperature(await AskAsync(session, request, log, token).ConfigureAwait(false)) is { } celsius)
                        temperatures.Add(celsius);
                soc = GM.DecodeSoc(await AskAsync(session, GM.BoltSoc, log, token).ConfigureAwait(false));
                packVoltage = GM.DecodePackVoltage(await AskAsync(session, GM.BoltPackVoltage, log, token).ConfigureAwait(false));
                current = GM.DecodeCurrent(await AskAsync(session, GM.BoltCurrent, log, token).ConfigureAwait(false));
                var from2019 = year is >= 2019;
                capacity = GM.DecodeCapacity(
                    await AskAsync(session, from2019 ? GM.BoltCapacityFrom2019 : GM.BoltCapacityTo2018, log, token).ConfigureAwait(false),
                    from2019);
            }

            if (temperatures.Count == 0 &&
                GM.DecodeTemperature(await AskAsync(session, GM.BatteryTemperature, log, token).ConfigureAwait(false)) is { } packTemperature)
                temperatures.Add(packTemperature);

            reading = new HvPackReading(
                HvPackKind.GmVoltBolt,
                "7E7 22 4181…4240",
                cells.ToArray(),
                temperatures.ToArray(),
                Array.Empty<int>(),
                soc,
                current,
                packVoltage,
                null,
                numbers.Count == GM.CellCount ? null : numbers.ToArray(),
                capacity);
        }, token).ConfigureAwait(false);

        // Cells are read one by one, so there is no load test for this pack.
        _sampler = null;
        _lastPackKind = reading?.Kind;
        return reading;
    }
}
