using System.Globalization;
using CarScanner.Models;

namespace CarScanner.Services.OBD;

/// <summary>One oxygen-sensor (or Mode 06) test row, ready to show.</summary>
public sealed record O2TestRow(string Title, string Subtitle, string ValueText, string LimitsText, bool Passed, string Explanation)
{
    public string StatusText => Passed ? ObdLang.L("ნორმაშია", "PASSED") : ObdLang.L("ნორმაში არ არის", "FAILED");
    public string StatusColor => Passed ? "#1F8A4C" : "#B3361F";
    public bool HasExplanation => !string.IsNullOrWhiteSpace(Explanation);
}

/// <summary>
/// Reads oxygen-sensor test results the way the car actually provides them:
/// • CAN cars (most cars from 2008): service $05 does not exist; the same O2
///   sensor and O2 heater tests are in service $06 (MIDs 01–10 and 41–50).
/// • Older non-CAN cars: service $05 with a test ID and sensor number
///   ("05 TID SENSOR"). A bare "05" — what the app sent before — is not a
///   valid request, which is why Mode 05 always looked unavailable.
/// Everything is read-only.
/// </summary>
public static class O2TestReader
{
    private static string L(string ka, string en) => ObdLang.L(ka, en);

    public sealed record Result(IReadOnlyList<O2TestRow> Rows, bool FromMode06, int SensorsChecked);

    public static async Task<Result> ReadAsync(OBDService obd, IProgress<(int Percent, string Text)>? progress, CancellationToken token)
    {
        return obd.IsCanProtocol
            ? await ReadCanAsync(obd, progress, token).ConfigureAwait(false)
            : await ReadLegacyAsync(obd, progress, token).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // CAN: O2 tests live in Mode 06
    // ------------------------------------------------------------------

    private static async Task<Result> ReadCanAsync(OBDService obd, IProgress<(int Percent, string Text)>? progress, CancellationToken token)
    {
        progress?.Report((20, L("O₂ მონიტორების სია (Mode 06)…", "O₂ monitor list (Mode 06)…")));
        var supported = new List<byte>();
        foreach (var baseMid in new byte[] { 0x00, 0x20, 0x40 })
        {
            token.ThrowIfCancellationRequested();
            var raw = await obd.ReadMode06MidAsync(baseMid, token).ConfigureAwait(false);
            var mids = Mode06Definitions.ParseSupported(raw, baseMid);
            supported.AddRange(mids.Where(IsO2Mid));
            if (!mids.Contains((byte)(baseMid + 0x20)))
                break;
        }

        var rows = new List<O2TestRow>();
        for (var index = 0; index < supported.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var mid = supported[index];
            progress?.Report((25 + 70 * (index + 1) / Math.Max(1, supported.Count), Mode06Definitions.MonitorName(mid)));
            try
            {
                var raw = await obd.ReadMode06MidAsync(mid, token).ConfigureAwait(false);
                foreach (var test in Mode06Definitions.ParseMid(raw, mid))
                {
                    rows.Add(new O2TestRow(
                        Mode06Definitions.MonitorName(mid),
                        $"{Mode06Definitions.TestName(test.TestId)} · TID ${test.TestId:X2}",
                        test.ActualText,
                        test.LimitsText,
                        test.Passed,
                        Mode06Definitions.Explain(mid, test.TestId, test.Passed)));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One monitor that does not answer does not stop the others.
            }
        }

        return new Result(rows, true, supported.Count(mid => mid <= 0x10));
    }

    private static bool IsO2Mid(byte mid) => mid is (>= 0x01 and <= 0x10) or (>= 0x41 and <= 0x50);

    // ------------------------------------------------------------------
    // Non-CAN: real Mode 05 requests
    // ------------------------------------------------------------------

    private static async Task<Result> ReadLegacyAsync(OBDService obd, IProgress<(int Percent, string Text)>? progress, CancellationToken token)
    {
        progress?.Report((15, L("ჟანგბადის სენსორების სია…", "Oxygen sensor list…")));
        var sensors = await O2SensorsAsync(obd, token).ConfigureAwait(false);

        var rows = new List<O2TestRow>();
        var checkedSensors = 0;
        for (var s = 0; s < sensors.Count; s++)
        {
            var (number, name) = sensors[s];
            var answered = false;
            for (byte tid = 0x01; tid <= 0x0A; tid++)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report((20 + 75 * (s * 10 + tid) / (sensors.Count * 10), $"{name} · TID ${tid:X2}"));

                // A sensor that answers neither the first constant nor the
                // first switch-time test is skipped quickly.
                if (!answered && tid is 0x02 or 0x03 or 0x04)
                    continue;
                if (!answered && tid > 0x05)
                    break;

                string raw;
                try
                {
                    raw = await obd.ReadMode05TestAsync(tid, number, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    continue;
                }

                var row = ParseMode05(raw, tid, number, name);
                if (row is null)
                    continue;
                if (!answered)
                {
                    answered = true;
                    checkedSensors++;
                    if (tid == 0x05)
                    {
                        // Sensor answers: go back for the constants 01–04.
                        for (byte constant = 0x01; constant <= 0x04; constant++)
                        {
                            try
                            {
                                var constantRaw = await obd.ReadMode05TestAsync(constant, number, token).ConfigureAwait(false);
                                if (ParseMode05(constantRaw, constant, number, name) is { } constantRow)
                                    rows.Add(constantRow);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                            }
                        }
                    }
                }
                rows.Add(row);
            }
        }

        return new Result(rows.OrderBy(r => r.Title).ToList(), false, checkedSensors);
    }

    /// <summary>O2 sensors from PID 13 (or 1D); numbering 1 = B1S1, 2 = B1S2 … 5 = B2S1.</summary>
    private static async Task<IReadOnlyList<(byte Number, string Name)>> O2SensorsAsync(OBDService obd, CancellationToken token)
    {
        var list = new List<(byte, string)>();
        try
        {
            var raw = await obd.ReadMode01PidQuickAsync(0x13, token).ConfigureAwait(false);
            var bytes = ObdResponseParser.ParseHex(raw);
            for (var i = 0; i + 2 < bytes.Length; i++)
            {
                if (bytes[i] != 0x41 || bytes[i + 1] != 0x13) continue;
                var mask = bytes[i + 2];
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((mask & (1 << bit)) != 0)
                        list.Add(((byte)(bit + 1), SensorName(bit)));
                }
                break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }

        if (list.Count == 0)
        {
            // Typical layout: one sensor before and one after the catalyst.
            list.Add((1, SensorName(0)));
            list.Add((2, SensorName(1)));
        }
        return list;
    }

    private static string SensorName(int bit)
    {
        var bank = bit / 4 + 1;
        var sensor = bit % 4 + 1;
        var position = sensor == 1 ? L("კატალიზატორამდე", "before catalyst") : L("კატალიზატორის შემდეგ", "after catalyst");
        return L($"ბანკი {bank}, სენსორი {sensor} ({position})", $"Bank {bank} Sensor {sensor} ({position})");
    }

    /// <summary>"45 TID SENSOR value [min max]" — one byte each.</summary>
    private static O2TestRow? ParseMode05(string raw, byte tid, byte sensor, string name)
    {
        if (string.IsNullOrWhiteSpace(raw) || ObdResponseParser.IsNegative(raw))
            return null;
        var bytes = ObdResponseParser.ParseHex(raw);
        for (var i = 0; i + 3 < bytes.Length; i++)
        {
            if (bytes[i] != 0x45 || bytes[i + 1] != tid || bytes[i + 2] != sensor)
                continue;

            var value = bytes[i + 3];
            var hasLimits = i + 5 < bytes.Length;
            var min = hasLimits ? bytes[i + 4] : (byte)0;
            var max = hasLimits ? bytes[i + 5] : (byte)0xFF;
            var passed = !hasLimits || (value >= min && value <= max);
            return new O2TestRow(
                name,
                $"{Mode06Definitions.TestName(tid)} · TID ${tid:X2}",
                FormatMode05(tid, value),
                hasLimits ? $"{L("მინ", "MIN")} {FormatMode05(tid, min)} · {L("მაქს", "MAX")} {FormatMode05(tid, max)}" : L("ზღვარი არ არის", "No limits"),
                passed,
                Mode06Definitions.Explain(0x01, tid, passed));
        }
        return null;
    }

    private static string FormatMode05(byte tid, byte value) => tid switch
    {
        0x01 or 0x02 or 0x03 or 0x04 or 0x07 or 0x08 => (value * 0.005).ToString("0.000", CultureInfo.InvariantCulture) + " V",
        0x05 or 0x06 or 0x09 or 0x0A => (value * 0.004).ToString("0.000", CultureInfo.InvariantCulture) + " s",
        _ => value.ToString(CultureInfo.InvariantCulture)
    };

    // ------------------------------------------------------------------
    // Non-CAN Mode 06 (older cars): "06 TID" → "46 TID CID V V L L"
    // ------------------------------------------------------------------

    public static async Task<IReadOnlyList<Mode06MonitorResult>> ReadMode06LegacyAsync(OBDService obd, IProgress<(int Percent, string Text)>? progress, CancellationToken token)
    {
        var tids = new List<byte>();
        foreach (var baseTid in new byte[] { 0x00, 0x20, 0x40, 0x60 })
        {
            token.ThrowIfCancellationRequested();
            var raw = await obd.ReadMode06TidAsync(baseTid, token).ConfigureAwait(false);
            var list = Mode06Definitions.ParseSupported(raw, baseTid);
            if (list.Count == 0) break;
            tids.AddRange(list.Where(tid => tid % 0x20 != 0));
            if (!list.Contains((byte)(baseTid + 0x20))) break;
        }

        var results = new List<Mode06MonitorResult>();
        for (var index = 0; index < tids.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var tid = tids[index];
            progress?.Report((20 + 75 * (index + 1) / tids.Count, $"TID ${tid:X2}"));
            try
            {
                var raw = await obd.ReadMode06TidAsync(tid, token).ConfigureAwait(false);
                var bytes = ObdResponseParser.ParseHex(raw);
                for (var i = 0; i + 6 < bytes.Length; i++)
                {
                    if (bytes[i] != 0x46 || bytes[i + 1] != tid) continue;
                    var cid = bytes[i + 2];
                    var value = (bytes[i + 3] << 8) | bytes[i + 4];
                    var limit = (bytes[i + 5] << 8) | bytes[i + 6];
                    // CID bit 7: 0 = the limit is a maximum, 1 = a minimum.
                    var isMinimum = (cid & 0x80) != 0;
                    results.Add(new Mode06MonitorResult(tid, (byte)(cid & 0x7F), isMinimum ? limit : 0, isMinimum ? 0xFFFF : limit, value, 0));
                    i += 6;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }
        }
        return results;
    }
}
