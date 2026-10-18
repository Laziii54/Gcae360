using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>An adapter whose data requests keep failing is reset and initialised again by the next operation (simulation only).</summary>
public sealed class AdapterSelfRecoveryTests
{
    private static async Task<(SimulatedElm327 Sim, Elm327Protocol Elm)> CreateAsync()
    {
        var sim = new SimulatedElm327();
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        await obd.InitializeAsync();
        sim.Sent.Clear();
        return (sim, elm);
    }

    private static async Task FailAsync(Elm327Protocol elm, int times)
    {
        for (var i = 0; i < times; i++)
            await Assert.ThrowsAsync<Elm327TimeoutException>(() => elm.SendCommandAsync("010C", TimeSpan.FromMilliseconds(60), 1, false));
    }

    [Fact]
    public async Task AfterAStreakOfTimeouts_TheNextOperationResetsAndReinitialisesTheAdapter()
    {
        var (sim, elm) = await CreateAsync();
        sim.Fault = SimFault.Timeout;
        await FailAsync(elm, 6);
        Assert.Equal(6, elm.FailureStreak);

        sim.Fault = SimFault.None;
        sim.Sent.Clear();
        var answer = await elm.SendCommandAsync("010C", TimeSpan.FromSeconds(2), 1, false);

        Assert.Contains("41 0C", answer);
        Assert.StartsWith("ATZ", sim.Sent[0]);          // reset first, then the request
        Assert.True(elm.IsInitialized);
        Assert.Equal(0, elm.FailureStreak);
    }

    [Fact]
    public async Task FewerFailuresThanTheThreshold_DoNotResetAnything()
    {
        var (sim, elm) = await CreateAsync();
        sim.Fault = SimFault.Timeout;
        await FailAsync(elm, 5);

        sim.Fault = SimFault.None;
        sim.Sent.Clear();
        await elm.SendCommandAsync("010C", TimeSpan.FromSeconds(2), 1, false);

        Assert.DoesNotContain(sim.Sent, command => command.StartsWith("ATZ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnswersFromAnEcuThatHasNoData_NeverCountAsAFailedAdapter()
    {
        var (sim, elm) = await CreateAsync();
        sim.Fault = SimFault.NoData;
        for (var i = 0; i < 12; i++)
            await elm.SendCommandAsync("010C", TimeSpan.FromSeconds(1), 1, false);   // a sweep over unsupported identifiers looks like this

        Assert.Equal(0, elm.FailureStreak);
    }

    [Fact]
    public async Task ARecoveryIsNotRepeatedImmediately()
    {
        var (sim, elm) = await CreateAsync();
        sim.Fault = SimFault.Timeout;
        await FailAsync(elm, 6);
        sim.Fault = SimFault.None;
        await elm.SendCommandAsync("010C", TimeSpan.FromSeconds(2), 1, false);   // recovers

        sim.Fault = SimFault.Timeout;
        await FailAsync(elm, 6);
        sim.Fault = SimFault.None;
        sim.Sent.Clear();
        await elm.SendCommandAsync("010C", TimeSpan.FromSeconds(2), 1, false);

        Assert.DoesNotContain(sim.Sent, command => command.StartsWith("ATZ", StringComparison.Ordinal));   // spaced out, not a reset loop
    }
}
