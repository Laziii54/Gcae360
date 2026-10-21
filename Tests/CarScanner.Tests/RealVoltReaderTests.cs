using System.Text;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;

namespace CarScanner.Tests;

/// <summary>
/// The REAL Chevrolet Volt / Bolt reader (<c>HybridHealthService.ReadGmPackAsync</c>: 96 cell requests to the battery module,
/// the thresholds, the messages it logs) and the real protocol layer, against simulated adapters over the direct link, the real
/// Wi-Fi transport and the real BLE code. The reader is linked unchanged; only a test-side stand-in supplies the parts of
/// the service that belong to the rest of the app (see HybridHealthServiceShim.cs).
/// SIMULATION ONLY. Cell 1, cell 96 and the battery temperature are the recorded OBDb answers used by GmVoltBoltBatteryTests;
/// the 94 other cell answers are made up (values near the recorded ones). Nothing here ran against a car.
/// </summary>
public sealed class RealVoltReaderTests
{
    private static readonly VehicleInfo Volt2012 = new() { Manufacturer = "Chevrolet", Model = "Volt", Year = "2012" };

    private static string CellFrame(int index)
    {
        var did = GmVoltBoltBattery.CellDid(index);
        // Cell 1 and cell 96 are the recorded answers (4.040 V and 4.036 V); the rest are made-up neighbours of them.
        var value = index == 0 ? 0xCED8 : index == 95 ? 0xCEA7 : 0xCE80 + (index % 9);
        return "7EF05" + "62" + did.ToString("X4") + value.ToString("X4");
    }

    /// <summary>A battery module that answers cells for which <paramref name="answers"/> is true (all by default) and the temperature.</summary>
    private static SimulatedElm327 Becm(Func<int, bool>? answers = null)
    {
        var sim = new SimulatedElm327();
        for (var index = 0; index < GmVoltBoltBattery.CellCount; index++)
            if (answers?.Invoke(index) ?? true)
                sim.ModuleAnswers[$"7E7|22{GmVoltBoltBattery.CellDid(index):X4}"] = CellFrame(index);
        sim.ModuleAnswers["7E4|22434F"] = "7EC0462434F3F";       // recorded: 63 - 40 = 23 °C
        return sim;
    }

    private sealed record Outcome(HybridHealthService.HvPackReading? Reading, string Log, Exception? Failure, IReadOnlyList<string> Sent);

    private static async Task<Outcome> ReadAsync(IBluetoothService link, SimulatedElm327? sim = null, Action<SimulatedElm327>? afterInit = null)
    {
        var rig = await AdapterRigs.CreateAsync(link);
        sim?.Sent.Clear();
        afterInit?.Invoke(sim!);
        var log = new StringBuilder();
        try
        {
            var reading = await new HybridHealthService(rig.Obd).ReadGmPackAsync(Volt2012, bolt: false, log, null, CancellationToken.None);
            return new Outcome(reading, log.ToString(), null, sim?.Sent.ToArray() ?? Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new Outcome(null, log.ToString(), ex, sim?.Sent.ToArray() ?? Array.Empty<string>());
        }
    }

    // ------------------------------------------------------------------ the car answers

    [Fact]
    public async Task AllNinetySixCells_ProduceAReading_WithTheRecordedCellsAndTemperature()
    {
        var sim = Becm();

        var result = await ReadAsync(sim, sim);

        Assert.Null(result.Failure);
        var reading = result.Reading!;
        Assert.Equal(HvPackKind.GmVoltBolt, reading.Kind);
        Assert.Equal(96, reading.Blocks.Length);
        Assert.Null(reading.BlockNumbers);                                        // all cells answered: numbered 1…96
        Assert.Equal(4.040, reading.Blocks[0], 3);                                 // the recorded cell 1
        Assert.Equal(4.036, reading.Blocks[95], 3);                                // the recorded cell 96
        Assert.InRange(reading.Blocks.Min(), 4.03, 4.05);
        Assert.Equal(new[] { 23d }, reading.Temperatures);                         // the recorded temperature
        Assert.Contains("96/96", result.Log);
        // 96 cell requests, one per cell, in order, to the battery module — and nothing else about the cells.
        Assert.Equal(Enumerable.Range(0, 96).Select(i => "22" + GmVoltBoltBattery.CellDid(i).ToString("X4")),
            result.Sent.Where(command => command.StartsWith("22", StringComparison.Ordinal) && command != "22434F" && command != "2241A3"));
    }

    [Fact]
    public async Task SomeCellsMissing_StillGivesAReading_ThatSaysWhichCellsAnswered()
    {
        var sim = Becm(index => index % 10 != 4);                                  // 10 cells (5, 15, … 95) do not answer: NO DATA

        var result = await ReadAsync(sim, sim);

        Assert.Null(result.Failure);
        Assert.Equal(86, result.Reading!.Blocks.Length);
        Assert.Equal(86, result.Reading.BlockNumbers!.Length);
        Assert.DoesNotContain(5, result.Reading.BlockNumbers);
        Assert.Contains("86/96", result.Log);
    }

    [Fact]
    public async Task TooFewCellsAnswering_ReturnsNoReading_AndLogsWhy()
    {
        var sim = Becm(index => index < 70);                                       // only 70 of 96

        var result = await ReadAsync(sim, sim);

        Assert.Null(result.Failure);
        Assert.Null(result.Reading);
        Assert.Contains("70/96", result.Log);
        Assert.Contains("ძალიან ბევრმა უჯრედმა არ უპასუხა", result.Log);
    }

    [Fact]
    public async Task AModuleThatAnswersNothingForTheFirstThreeCells_EndsTheReadAfterThree_AndSaysSo()
    {
        var sim = Becm(_ => false);

        var result = await ReadAsync(sim, sim);

        Assert.Null(result.Reading);
        Assert.Contains("BECM (7E7) უჯრედების ძაბვას არ აბრუნებს", result.Log);
        Assert.Equal(3, result.Sent.Count(command => command.StartsWith("2241", StringComparison.Ordinal)));   // it stopped after three
    }

    [Fact]
    public async Task ABatteryModuleThatIsWakingUp_AnsweringZeroVolts_IsNotAReading()
    {
        var sim = new SimulatedElm327();
        for (var index = 0; index < GmVoltBoltBattery.CellCount; index++)
            sim.ModuleAnswers[$"7E7|22{GmVoltBoltBattery.CellDid(index):X4}"] = "7EF05" + "62" + GmVoltBoltBattery.CellDid(index).ToString("X4") + "0000";

        var result = await ReadAsync(sim, sim);

        Assert.Null(result.Reading);                                                // a value that is not plausible is never used
    }

    [Fact]
    public async Task ANegativeResponseForEveryCell_IsNotAReading()
    {
        var sim = new SimulatedElm327();
        for (var index = 0; index < GmVoltBoltBattery.CellCount; index++)
            sim.ModuleAnswers[$"7E7|22{GmVoltBoltBattery.CellDid(index):X4}"] = "7EF037F2231";      // request out of range

        var result = await ReadAsync(sim, sim);

        Assert.Null(result.Reading);
    }

    [Fact]
    public async Task AResponsePending_IsAskedAgainWithALongerWait_AndTheCellIsRead()
    {
        var sim = Becm();
        sim.ModuleAnswers["7E7|224181"] = "7EF 05 62 41 81 CE D8";                 // the simulator builds "pending" from a spaced frame
        sim.PendingBeforeAnswer = 1;                                               // the first request is answered "response pending"

        var result = await ReadAsync(sim, sim);

        Assert.Equal(96, result.Reading!.Blocks.Length);
        Assert.Equal(2, result.Sent.Count(command => command == "224181"));          // asked once, pending, asked again
    }

    [Fact]
    public async Task FiveCellRequestsThatTimeOutInARow_LoseFiveCells_AndTheRestIsStillRead()
    {
        var sim = Becm();
        var scripted = new ScriptedAdapter(sim);
        for (var index = 10; index < 15; index++)
            scripted.Fail("22" + GmVoltBoltBattery.CellDid(index).ToString("X4"), () => new TimeoutException("simulated"));

        var result = await ReadAsync(scripted, sim);

        Assert.Null(result.Failure);
        Assert.Equal(91, result.Reading!.Blocks.Length);
        Assert.DoesNotContain(11, result.Reading.BlockNumbers!);
        Assert.Contains("91/96", result.Log);
    }

    // ------------------------------------------------------------------ the adapter is the difference

    [Fact]
    public async Task AdapterThatRefusesAts0_ThrowsTheTechnicalError_SoThePageShowsItNotTheReadyMessage()
    {
        var sim = Becm();
        sim.RejectedAt.Add("ATS0");

        var result = await ReadAsync(sim, sim);

        var failure = Assert.IsType<Elm327CommandException>(result.Failure);
        Assert.Equal("ATS0", failure.UnsupportedCommand);
        Assert.Null(result.Reading);
        Assert.DoesNotContain(result.Sent, command => command.StartsWith("2241", StringComparison.Ordinal));   // no cell was asked
    }

    [Fact]
    public async Task AnAdapterThatEchoesEveryCommand_LosesEveryCell()
    {
        var sim = Becm();

        var result = await ReadAsync(new EchoingAdapter(sim), sim);

        Assert.Null(result.Reading);
        Assert.Contains("BECM (7E7) უჯრედების ძაბვას არ აბრუნებს", result.Log);
    }

    [Fact]
    public async Task AnAdapterThatIgnoresTheHeader_ReachesTheEngineEcuInstead_AndLosesEveryCell()
    {
        var sim = new SimulatedElm327();
        for (var index = 0; index < GmVoltBoltBattery.CellCount; index++)
            sim.ModuleAnswers[$"7DF|22{GmVoltBoltBattery.CellDid(index):X4}"] = "7E8 06 62 DD 01 03 B5 8F";   // whoever answers a functional request
        using var log = new LogScope();

        var result = await ReadAsync(new HeaderIgnoringAdapter(sim), sim);

        Assert.Null(result.Reading);
        var summary = log.Find("Module session over")!;
        Assert.Contains("frames from 7E8 x3", summary);                             // the evidence the page's message does not give
        Assert.Contains("3 replies came only from other modules", summary);
    }

    [Fact]
    public async Task OnAChipWithoutReceiveFilters_WhenTheEngineEcuAnswersFirstToo_EveryCellIsStillRead()
    {
        // Stage 2C: this used to lose every cell (the app-side filter judged the line the protocol layer had joined by its first
        // frame, so the engine ECU's frame took the BECM's frame down with it). The filter now works frame by frame.
        var sim = new SimulatedElm327();
        foreach (var command in new[] { "ATCRA", "ATAR", "ATCF" })
            sim.RejectedAt.Add(command);
        for (var index = 0; index < GmVoltBoltBattery.CellCount; index++)
            sim.ModuleAnswers[$"7E7|22{GmVoltBoltBattery.CellDid(index):X4}"] = "7E8 05 62 41 81 AA BB\r" + CellFrame(index);

        var result = await ReadAsync(sim, sim);

        Assert.Null(result.Failure);
        Assert.Equal(96, result.Reading!.Blocks.Length);                            // the BECM's frame is found behind the engine ECU's
        Assert.Equal(4.040, result.Reading.Blocks[0], 3);                            // the recorded cell 1, not a byte taken from the engine's frame
    }

    // ------------------------------------------------------------------ the same car, the same adapter, three ways to reach it

    [Theory]
    [MemberData(nameof(TransportRunner.All), MemberType = typeof(TransportRunner))]
    public async Task TheReading_IsTheSame_OverTheDirectLink_OverWifi_AndOverBle(SimTransport transport)
    {
        var sim = Becm();

        var reading = await TransportRunner.RunAsync(transport, sim, async rig =>
        {
            var found = await new HybridHealthService(rig.Obd).ReadGmPackAsync(Volt2012, false, new StringBuilder(), null, CancellationToken.None);
            return found;
        });

        Assert.NotNull(reading);
        Assert.Equal(Enumerable.Range(0, 96).Select(i => Math.Round(
            (i == 0 ? 0xCED8 : i == 95 ? 0xCEA7 : 0xCE80 + (i % 9)) * 5d / 65535d, 4)), reading!.Blocks);
        Assert.Equal(new[] { 23d }, reading.Temperatures);
    }

    [Theory]
    [MemberData(nameof(TransportRunner.All), MemberType = typeof(TransportRunner))]
    public async Task AnAdapterThatRefusesAts0_IsRefusedTheSameWay_OverEveryTransport(SimTransport transport)
    {
        var sim = Becm();
        sim.RejectedAt.Add("ATS0");

        var failure = await TransportRunner.RunAsync(transport, sim, async rig =>
            await Record.ExceptionAsync(() => new HybridHealthService(rig.Obd).ReadGmPackAsync(Volt2012, false, new StringBuilder(), null, CancellationToken.None)));

        var refused = Assert.IsType<Elm327CommandException>(failure);
        Assert.Equal("ATS0", refused.UnsupportedCommand);
    }

    [Theory]
    [MemberData(nameof(TransportRunner.All), MemberType = typeof(TransportRunner))]
    public async Task AnEchoingAdapter_LosesTheCellsTheSameWay_OverEveryTransport(SimTransport transport)
    {
        var sim = Becm();

        var reading = await TransportRunner.RunAsync(transport, new EchoingAdapter(sim), async rig =>
            await new HybridHealthService(rig.Obd).ReadGmPackAsync(Volt2012, false, new StringBuilder(), null, CancellationToken.None));

        Assert.Null(reading);
    }

    // ------------------------------------------------------------------ what the page tells the person

    [Fact]
    public void WhenTheReaderReturnsNoPack_TheServiceDecidesTheSentenceFromTheEvidence_AndTheReaderStillThrowsOnARefusedSetupCommand()
    {
        // Pinned from the source (the scan service and the page are not part of this project). Before Stage 2A every way the
        // read could fail ended in "the battery did not answer, switch the car on (READY)", except a refused setup command,
        // whose exception reached the page's catch and was shown as the exception's own text. Now the service tells the causes
        // the evidence decides apart (see HybridFailureReportingTests), keeps the old sentence as the fallback, and still lets
        // every other exception and cancellation reach the page's handler. The reader itself still throws on a refused command
        // (the tests above), so what it does is unchanged.
        var service = File.ReadAllText(RepoFile("Services/HybridHealthService.cs")).Replace("\r\n", "\n");
        var page = File.ReadAllText(RepoFile("ViewModels/HybridHealthViewModel.cs")).Replace("\r\n", "\n");

        var branch = service[service.IndexOf("pack = await ReadGmPackAsync(", StringComparison.Ordinal)..];
        branch = branch[..branch.IndexOf("if (pack is not null)", StringComparison.Ordinal)];
        Assert.Contains("if (pack is null && (GmKind(vehicle) is not null || profile == HybridProfileKind.ChevroletVolt))", branch);
        Assert.Contains("HybridReadDiagnosis.Decide(", branch);
        Assert.DoesNotContain("ბატარეამ არ უპასუხა", branch);                          // the old sentence is now the diagnosis' fallback
        Assert.Contains("\"ბატარეამ არ უპასუხა. ჩართე მანქანა (READY) და სცადე თავიდან.\"", File.ReadAllText(RepoFile("Services/HybridReadDiagnosis.cs")).Replace("\r\n", "\n"));

        Assert.Contains("ScanStage = L($\"შემოწმება ვერ დასრულდა: {ex.Message}\", $\"Check failed: {ex.Message}\");", page);
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, relative);
    }
}
