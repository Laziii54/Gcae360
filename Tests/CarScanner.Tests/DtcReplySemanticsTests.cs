using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// "The ECU answered and lists no codes" (43 00) and "nobody answered" (NO DATA) are different facts.
/// Simulation only: it proves the app tells them apart, not how a particular car behaves.
/// </summary>
public sealed class DtcReplySemanticsTests
{
    private static async Task<OBDService> CreateAsync(Action<SimulatedElm327> configure)
    {
        var sim = new SimulatedElm327();
        sim.ObdAnswers["0100"] = "41 00 00 18 00 01";
        configure(sim);
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        await obd.InitializeAsync();
        return obd;
    }

    [Fact]
    public async Task ReplyListingZeroCodes_IsAReply_NotAMissingAnswer()
    {
        var obd = await CreateAsync(s => s.ObdAnswers["03"] = "43 00");
        var codes = await obd.ReadStoredDtcAsync();
        Assert.Empty(codes);
        Assert.False(obd.LastDtcReadGotNoReply("03"));
    }

    [Fact]
    public async Task NoData_IsReportedAsNoReply_NotAsNoCodes()
    {
        var obd = await CreateAsync(s => s.ObdAnswers["03"] = "NO DATA");
        var codes = await obd.ReadStoredDtcAsync();
        Assert.Empty(codes);                                  // there is nothing to list ...
        Assert.True(obd.LastDtcReadGotNoReply("03"));         // ... but the caller can now see that nothing answered
    }

    [Fact]
    public async Task ARealReplyAfterANoReply_ClearsTheFlag()
    {
        var obd = await CreateAsync(s => s.ObdAnswers["03"] = "NO DATA");
        await obd.ReadStoredDtcAsync();
        Assert.True(obd.LastDtcReadGotNoReply("03"));

        var recovered = await CreateAsync(s => s.ObdAnswers["03"] = "43 01 01 33 00 00 00");
        var codes = await recovered.ReadStoredDtcAsync();
        Assert.Single(codes);
        Assert.Equal("P0133", codes[0].Code);
        Assert.False(recovered.LastDtcReadGotNoReply("03"));
    }

    [Fact]
    public async Task ServicesAreTrackedIndependently()
    {
        var obd = await CreateAsync(s =>
        {
            s.ObdAnswers["03"] = "43 00";
            s.ObdAnswers["0A"] = "NO DATA";
        });
        await obd.ReadStoredDtcAsync();
        await obd.ReadPermanentDtcAsync();
        Assert.False(obd.LastDtcReadGotNoReply("03"));
        Assert.True(obd.LastDtcReadGotNoReply("0A"));
        Assert.False(obd.LastDtcReadGotNoReply("07"));        // never read
    }
}
