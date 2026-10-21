using System.Globalization;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Services;

/// <summary>Battery layouts the app reads directly from the HV battery ECU.</summary>
public enum HvPackKind
{
    /// <summary>Toyota / Lexus 2004–2009 (Prius XW20 …): NiMH blocks, ECU 7E3.</summary>
    ToyotaGen2NiMh,
    /// <summary>Toyota / Lexus ≈2010–2018 (Prius XW30, Camry, RAV4 …): NiMH blocks, ECU 7E2 / 7E3.</summary>
    ToyotaGen3NiMh,
    /// <summary>Toyota / Lexus 2017+ (TNGA): individual cells, ECU 747.</summary>
    ToyotaCells,
    /// <summary>Hyundai / Kia / Genesis hybrids, plug-ins and EVs: cells, BMS 7E4.</summary>
    HyundaiKia,
    /// <summary>Chevrolet Volt / Opel Ampera / Cadillac ELR and Chevrolet Bolt: 96 cells, one request each, BECM 7E7.</summary>
    GmVoltBolt,
    /// <summary>Nissan Leaf / e-NV200: 96 cells in one request, SOH, battery controller 79B.</summary>
    NissanLeaf,
    /// <summary>BMW i3: SOH, highest / lowest cell, cell capacity (BMW extended addressing, 6F1 → 07).</summary>
    BmwI3,
    /// <summary>Ford Fusion / C-Max Hybrid and Energi: module voltage spread, lowest / average module (7E4).</summary>
    FordHybrid,
    /// <summary>VW e-Golf: highest / lowest cell, SOC (BMS 7E5).</summary>
    VwEGolf,
    /// <summary>Audi e-tron (first generation): module cells, SOH, highest / lowest cell (BMS 7E5).</summary>
    AudiEtron,
    /// <summary>Haval Jolion HEV: SOH, highest / lowest cell (BMS 78B).</summary>
    HavalHev,
    /// <summary>Honda CR-V / Civic / Accord hybrid 2023+: all cells in one answer (29-bit 18DA01F1).</summary>
    HondaHybrid,
    /// <summary>Jaguar I-PACE: highest / lowest cell, SOC, SOH (7E4).</summary>
    JaguarIPace,
    /// <summary>Porsche Taycan: 33 modules × 6 cells, same BMS layout as the first Audi e-tron.</summary>
    PorscheTaycan,
    /// <summary>MG ZS EV: pack SOC / SOH and per-module min / max cell.</summary>
    MgZsEv
}

/// <summary>
/// One read-only request to a battery ECU. <paramref name="ExtendedAddress"/> is
/// BMW-style CAN extended addressing (the target ECU byte); its answers start
/// with the tester address (F1), which is removed before decoding.
/// </summary>
public sealed record HvRequest(int Tx, int Rx, byte[] Payload, byte? ExtendedAddress = null, bool Ext29 = false)
{
    public string Label => $"{(Ext29 ? Tx.ToString("X8") : Tx.ToString("X3"))}{(ExtendedAddress is { } target ? $"/{target:X2}" : "")} {string.Concat(Payload.Select(b => b.ToString("X2")))}";
}

/// <summary>One reading of the whole pack (blocks or cells, in volts).</summary>
public sealed record HvPackSample(
    double[] Blocks,
    double? SocPercent,
    double? CurrentA);

/// <summary>ELM output (ATH1 ATS0) → ISO-TP message → data bytes.</summary>
public static class HvPackProtocol
{
    public static bool IsCellLevel(HvPackKind kind) => kind is HvPackKind.ToyotaCells or HvPackKind.HyundaiKia or HvPackKind.GmVoltBolt
        or HvPackKind.NissanLeaf or HvPackKind.BmwI3 or HvPackKind.FordHybrid or HvPackKind.VwEGolf
        or HvPackKind.AudiEtron or HvPackKind.HavalHev or HvPackKind.HondaHybrid or HvPackKind.JaguarIPace
        or HvPackKind.PorscheTaycan or HvPackKind.MgZsEv;

    /// <summary>
    /// False where cells are read one request at a time (GM): a sweep takes
    /// seconds, so cells would be read at different loads and the spread
    /// under load would be meaningless.
    /// </summary>
    public static bool SupportsLoadTest(HvPackKind kind) =>
        kind is not (HvPackKind.GmVoltBolt or HvPackKind.VwEGolf or HvPackKind.AudiEtron or HvPackKind.HavalHev
            or HvPackKind.PorscheTaycan or HvPackKind.MgZsEv or HvPackKind.JaguarIPace);

    /// <summary>
    /// Data after the positive response (61 xx / 62 xx xx) from the frames of
    /// <paramref name="request"/>'s receive ID, or null when the ECU did not answer it.
    /// </summary>
    public static byte[]? ExtractData(string? raw, HvRequest request)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var width = request.Ext29 ? 8 : 3;
        var header = request.Rx.ToString(request.Ext29 ? "X8" : "X3", CultureInfo.InvariantCulture);
        var frames = new List<byte[]>();

        // The text of ONE frame of this request's reply id (spaces between bytes, ATS1, are removed); anything else is skipped.
        void AddFrame(string frameText)
        {
            var text = frameText.Replace(" ", string.Empty).Replace("\t", string.Empty).Trim().ToUpperInvariant();
            if (text.Length <= width || (text.Length - width) % 2 != 0 || !text.StartsWith(header, StringComparison.Ordinal) || !text.All(Uri.IsHexDigit))
                return;
            var frame = new byte[(text.Length - width) / 2];
            for (var i = 0; i < frame.Length; i++)
                frame[i] = byte.Parse(text.AsSpan(width + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            // Extended addressing: every answer frame starts with the tester address (F1).
            if (request.ExtendedAddress is not null)
            {
                if (frame.Length < 2 || frame[0] != 0xF1)
                    return;
                frame = frame[1..];
            }
            frames.Add(frame);
        }

        foreach (var line in raw.Split(new[] { '\r', '\n', '>' }, StringSplitOptions.RemoveEmptyEntries))
        {
            // The protocol layer joins the adapter's lines with spaces, so one line may hold several frames, possibly from several
            // modules. They are split into frames first, each tied to the id that sent it, and only this request's id is kept: frames
            // of other modules are dropped, never merged into the message. Text that is not made of frames alone (an echoed command,
            // a status word, an impossible token) is not split and is handled as a single line as before.
            if (ModuleReplyAnalysis.SplitFrameTexts(line, request.Ext29) is { Count: >= 2 } several)
            {
                foreach (var part in several.Where(item => string.Equals(item.Header, header, StringComparison.Ordinal)))
                    AddFrame(part.Text);
                continue;
            }

            // One frame per line.
            AddFrame(line);
        }

        var positive = (byte)(request.Payload[0] + 0x40);
        foreach (var message in Reassemble(frames))
        {
            if (message.Length < request.Payload.Length || message[0] != positive)
                continue;
            var matches = true;
            for (var i = 1; i < request.Payload.Length; i++)
                matches &= message[i] == request.Payload[i];
            if (matches)
                return message[request.Payload.Length..];
        }

        return null;
    }

    /// <summary>True when the ECU asked for more time (7F xx 78).</summary>
    public static bool IsResponsePending(string? raw, HvRequest request)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var compact = raw.Replace(" ", string.Empty).ToUpperInvariant();
        return compact.Contains($"7F{request.Payload[0]:X2}78", StringComparison.Ordinal);
    }

    private static IEnumerable<byte[]> Reassemble(IReadOnlyList<byte[]> frames)
    {
        List<byte>? buffer = null;
        var expected = 0;
        var sequence = 0;
        foreach (var frame in frames)
        {
            if (frame.Length == 0)
                continue;
            switch (frame[0] >> 4)
            {
                case 0:
                {
                    var length = frame[0] & 0x0F;
                    if (length > 0 && length <= frame.Length - 1)
                        yield return frame[1..(1 + length)];
                    buffer = null;
                    break;
                }
                case 1 when frame.Length >= 2:
                    expected = ((frame[0] & 0x0F) << 8) | frame[1];
                    buffer = new List<byte>(expected);
                    buffer.AddRange(frame.Skip(2));
                    sequence = 1;
                    break;
                case 2 when buffer is not null:
                    if ((frame[0] & 0x0F) != (sequence & 0x0F))
                    {
                        buffer = null;
                        break;
                    }
                    buffer.AddRange(frame.Skip(1));
                    sequence++;
                    break;
            }

            if (buffer is not null && expected > 0 && buffer.Count >= expected)
            {
                yield return buffer.Take(expected).ToArray();
                buffer = null;
            }
        }
    }

    public static int U16(byte[] data, int index) => (data[index] << 8) | data[index + 1];
    public static int S16(byte[] data, int index) => (short)U16(data, index);
    public static int S8(byte[] data, int index) => (sbyte)data[index];
}

/// <summary>Judging a pack from the spread of its blocks / cells.</summary>
public static class HvPackAssessment
{
    public enum Verdict { Good, Watch, Weak, Failing }

    /// <summary>
    /// Spread (max − min) limits. NiMH blocks: Prius technicians treat
    /// &lt; 0.3 V at rest as healthy, and the battery ECU raises P0A80 at
    /// roughly 1.2 V; under load the spread is naturally larger. Lithium cells
    /// are much tighter (Hyundai / Kia report them in 20 mV steps, so one step
    /// is not a finding). Guide values, not a capacity test.
    /// </summary>
    public static (double Good, double Watch, double Weak) Limits(bool cells, bool underLoad) => (cells, underLoad) switch
    {
        (false, false) => (0.30, 0.50, 1.00),
        (false, true) => (0.50, 0.90, 1.20),
        (true, false) => (0.04, 0.08, 0.15),
        (true, true) => (0.08, 0.15, 0.25)
    };

    public static Verdict Judge(double spread, bool cells, bool underLoad)
    {
        var (good, watch, weak) = Limits(cells, underLoad);
        return spread <= good + 1e-9 ? Verdict.Good : spread <= watch + 1e-9 ? Verdict.Watch : spread <= weak + 1e-9 ? Verdict.Weak : Verdict.Failing;
    }

    /// <summary>0–100 from the spread: 100 at zero, 80 at the "good" limit, 60 at "watch", 35 at "weak".</summary>
    public static int Score(double spread, bool cells, bool underLoad)
    {
        var (good, watch, weak) = Limits(cells, underLoad);
        double score = spread switch
        {
            _ when spread <= good => 100 - spread / good * 20,
            _ when spread <= watch => 80 - (spread - good) / (watch - good) * 20,
            _ when spread <= weak => 60 - (spread - watch) / (weak - watch) * 25,
            _ => Math.Max(5, 35 - (spread - weak) / weak * 30)
        };
        return (int)Math.Round(Math.Clamp(score, 0, 100));
    }

    /// <summary>The block that sits clearly below the others (1-based), or null.</summary>
    public static (int? Block, double Deviation) WeakestBlock(IReadOnlyList<double> blocks, bool cells)
    {
        if (blocks.Count < 3)
            return (null, 0);
        var median = Median(blocks);
        var index = 0;
        for (var i = 1; i < blocks.Count; i++)
            if (blocks[i] < blocks[index])
                index = i;
        var deviation = Math.Round(median - blocks[index], 3);
        return deviation >= (cells ? 0.06 : 0.15) ? (index + 1, deviation) : (null, deviation);
    }

    /// <summary>Blocks (1-based) whose internal resistance is clearly above the others.</summary>
    public static int[] HighResistanceBlocks(IReadOnlyList<int> resistances)
    {
        if (resistances.Count < 3)
            return Array.Empty<int>();
        var median = Median(resistances.Select(value => (double)value).ToArray());
        return resistances
            .Select((value, index) => (value, index))
            .Where(item => item.value >= median * 1.3 && item.value >= median + 3)
            .Select(item => item.index + 1)
            .ToArray();
    }

    public static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        return sorted.Length == 0 ? 0 : sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}

/// <summary>Accumulates samples during the load test.</summary>
public sealed class HvLoadTracker
{
    private readonly bool _cells;
    private double[]? _deviationSum;
    private double[]? _worstSample;

    public HvLoadTracker(bool cells) => _cells = cells;

    public int Samples { get; private set; }
    public double MaxSpread { get; private set; }
    public double LastSpread { get; private set; }
    public double? MaxDischargeA { get; private set; }
    public double? LastCurrentA { get; private set; }

    /// <summary>Block values of the sample with the largest spread.</summary>
    public double[] WorstSample => _worstSample ?? Array.Empty<double>();

    public void Add(HvPackSample sample)
    {
        // Two values = highest / lowest cell of one answer (BMW i3, Leaf, Ford).
        if (sample.Blocks.Length < 2)
            return;
        if (_deviationSum is not null && _deviationSum.Length != sample.Blocks.Length)
            return; // a partial answer; ignore

        _deviationSum ??= new double[sample.Blocks.Length];
        var median = HvPackAssessment.Median(sample.Blocks);
        for (var i = 0; i < sample.Blocks.Length; i++)
            _deviationSum[i] += median - sample.Blocks[i];

        LastSpread = Math.Round(sample.Blocks.Max() - sample.Blocks.Min(), 3);
        if (LastSpread >= MaxSpread)
        {
            MaxSpread = LastSpread;
            _worstSample = sample.Blocks.ToArray();
        }

        if (sample.CurrentA is { } current)
        {
            LastCurrentA = current;
            if (current > (MaxDischargeA ?? double.MinValue))
                MaxDischargeA = current;
        }

        Samples++;
    }

    /// <summary>The block that was lowest on average over the whole test (1-based), if clearly lower.</summary>
    public (int? Block, double Deviation) WeakestBlock()
    {
        if (_deviationSum is null || Samples == 0 || _deviationSum.Length < 3)
            return (null, 0);
        var index = 0;
        for (var i = 1; i < _deviationSum.Length; i++)
            if (_deviationSum[i] > _deviationSum[index])
                index = i;
        var mean = Math.Round(_deviationSum[index] / Samples, 3);
        return mean >= (_cells ? 0.06 : 0.15) ? (index + 1, mean) : (null, mean);
    }
}
