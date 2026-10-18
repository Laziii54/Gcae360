using CarScanner.Services.OBD;

namespace CarScanner.Tests;

/// <summary>The ECU identifiers page: padding bytes of a multi-frame reply must not appear as values.</summary>
public sealed class Mode09FormatTests
{
    [Fact]
    public void Cvn_StopsAtTheAnnouncedLength_NoPaddingValues()
    {
        // 0x0B bytes: 49 06 02 + two CVNs; the last frame is padded with AA.
        var raw = "00B\r0: 49 06 02 08 00 00\r1: 6B 49 06 04 00 AA AA\r";
        Assert.Equal("0800006B, 49060400", ObdResponseParser.FormatMode09(raw, 0x06));
    }

    [Fact]
    public void EcuNames_AreSplitPerTwentyBytes_AndSeparated()
    {
        var name1 = "ECM".PadRight(20, '\0');
        var name2 = "TCM".PadRight(20, '\0');
        var bytes = new List<byte> { 0x49, 0x0A, 0x02 };
        bytes.AddRange(name1.Select(c => (byte)c));
        bytes.AddRange(name2.Select(c => (byte)c));
        var hex = bytes.Select(b => b.ToString("X2")).ToList();
        var lines = new List<string> { bytes.Count.ToString("X3") };
        var index = 0;
        for (var i = 0; i < hex.Count; i += i == 0 ? 6 : 7)
        {
            var take = Math.Min(i == 0 ? 6 : 7, hex.Count - i);
            lines.Add($"{(index++ % 16):X}: " + string.Join(" ", hex.Skip(i).Take(take)));
        }

        Assert.Equal("ECM, TCM", ObdResponseParser.FormatMode09(string.Join("\r", lines), 0x0A));
    }

    [Fact]
    public void NothingUsable_GivesEmpty() => Assert.Equal(string.Empty, ObdResponseParser.FormatMode09("NO DATA", 0x06));
}
