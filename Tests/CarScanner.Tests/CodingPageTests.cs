using System.Text.Json;
using CarScanner.Services.Coding;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// The Coding page: how module answers are read (the engine ECU's frames must never be stored as another module's
/// data or taken as the acknowledgement of a write), which features are offered for the facts at hand, and the
/// invariants of the verified-settings catalog. Nothing here talks to a car.
/// </summary>
public sealed class CodingReplyParserTests
{
    private static readonly byte[] PartNumberId = { 0xF1, 0x87 };

    [Fact]
    public void AnswerFromTheAskedModule_IsAccepted()
    {
        var reply = CodingReplyParser.Parse("7E8 06 62 F1 87 41 42 43", 0x7E8, 0x22, PartNumberId);

        Assert.Equal(new byte[] { 0x41, 0x42, 0x43 }, reply.Data);
        Assert.Equal(string.Empty, reply.Error);
        Assert.False(reply.IsForeign);
    }

    [Fact]
    public void EngineFramesDeliveredForAnotherModulesRequest_AreNotThatModulesData()
    {
        // Real captures: the engine ECU (0x7E8) answered requests addressed to 0x7E1 (reply id 0x7E9) and many others.
        var reply = CodingReplyParser.Parse("7E8 06 62 F1 87 41 42 43", 0x7E9, 0x22, PartNumberId);

        Assert.Null(reply.Data);
        Assert.True(reply.IsForeign);
        Assert.Equal(0x7E8, reply.ForeignSender);
        Assert.Contains("another module", reply.Error);
    }

    [Fact]
    public void AForeignWriteAcknowledgement_IsNotAConfirmation()
    {
        // 6E DD 01 from 0x7E8 while the write went to 0x726 (reply id 0x72E): the write is NOT confirmed.
        var reply = CodingReplyParser.Parse("7E8 03 6E DD 01", 0x72E, 0x2E, new byte[] { 0xDD, 0x01 });

        Assert.Null(reply.Data);
        Assert.True(reply.IsForeign);
    }

    [Fact]
    public void OwnAnswerAmongForeignFrames_IsStillFound()
    {
        var reply = CodingReplyParser.Parse("7E8 03 7F 22 31 7E9 06 62 F1 87 41 42 43", 0x7E9, 0x22, PartNumberId);

        Assert.Equal(new byte[] { 0x41, 0x42, 0x43 }, reply.Data);
    }

    [Fact]
    public void ForeignRefusal_IsNotThisModulesRefusal()
    {
        var reply = CodingReplyParser.Parse("7E8 03 7F 22 11", 0x708, 0x22, PartNumberId);

        Assert.Null(reply.Nrc);
        Assert.True(reply.IsForeign);
    }

    [Fact]
    public void OwnRefusal_ReportsItsCode()
    {
        var reply = CodingReplyParser.Parse("7E9 03 7F 22 31", 0x7E9, 0x22, PartNumberId);

        Assert.Null(reply.Data);
        Assert.Equal((byte)0x31, reply.Nrc);
        Assert.Equal("NRC 31", reply.Error);
    }

    [Fact]
    public void ResponsePending_IsNotARefusal_AndTheFinalAnswerIsUsed()
    {
        var reply = CodingReplyParser.Parse("7E8 03 7F 22 78 7E8 06 62 F1 87 41 42 43", 0x7E8, 0x22, PartNumberId);

        Assert.Equal(new byte[] { 0x41, 0x42, 0x43 }, reply.Data);
    }

    [Theory]
    [InlineData("NO DATA")]
    [InlineData("")]
    [InlineData("CAN ERROR")]
    public void Silence_IsNoAnswer(string raw)
    {
        var reply = CodingReplyParser.Parse(raw, 0x7E8, 0x22, PartNumberId);

        Assert.Null(reply.Data);
        Assert.Equal("no answer", reply.Error);
    }

    [Fact]
    public void MultiFrameAnswer_IsReassembled()
    {
        // 62 F1 90 + "WVWZZZ1" (10 bytes announced).
        var reply = CodingReplyParser.Parse("7E8 10 0A 62 F1 90 57 56 57 7E8 21 5A 5A 5A 31", 0x7E8, 0x22, new byte[] { 0xF1, 0x90 });

        Assert.Equal("WVWZZZ1", System.Text.Encoding.ASCII.GetString(reply.Data!));
    }

    [Fact]
    public void AdapterWithoutIds_IsJudgedByItsContent()
    {
        var reply = CodingReplyParser.Parse("06 62 F1 87 41 42 43", 0x7E8, 0x22, PartNumberId);

        Assert.Equal(new byte[] { 0x41, 0x42, 0x43 }, reply.Data);
    }

    [Fact]
    public void WithoutAReplyId_AnyResponderIsAccepted_AsBefore()
    {
        var reply = CodingReplyParser.Parse("7E8 06 62 F1 87 41 42 43", null, 0x22, PartNumberId);

        Assert.NotNull(reply.Data);
    }

    [Fact]
    public void AnswerForAnotherIdentifier_IsNotAcceptedAsThisOne()
    {
        var reply = CodingReplyParser.Parse("7E8 06 62 F1 88 41 42 43", 0x7E8, 0x22, PartNumberId);

        Assert.Null(reply.Data);
    }

    [Fact]
    public void PositiveWriteReplyWithoutData_IsAnEmptyArray_NotNull()
    {
        var reply = CodingReplyParser.Parse("7E8 03 6E DD 01", 0x7E8, 0x2E, new byte[] { 0xDD, 0x01 });

        Assert.NotNull(reply.Data);
        Assert.Empty(reply.Data!);
    }
}

public sealed class CodingCapabilityTests
{
    private static CodingInputs Inputs(bool connected = true, bool can = true, int forCar = 0, int inCatalog = 0, int backups = 0, int restorable = 0, bool pro = true) =>
        new(connected, can, VinKnown: true, forCar, inCatalog, backups, restorable, pro);

    private static CodingFeatureStatus Status(CodingInputs input, CodingFeature feature) =>
        CodingCapabilities.Evaluate(input).Single(item => item.Feature == feature);

    [Fact]
    public void Backup_NeedsAConnectedCanAdapter()
    {
        Assert.Equal(CodingFeatureState.Available, Status(Inputs(), CodingFeature.Backup).State);
        Assert.Equal(CodingFeatureState.NeedsConnection, Status(Inputs(connected: false), CodingFeature.Backup).State);
        Assert.Equal(CodingFeatureState.NotCan, Status(Inputs(can: false), CodingFeature.Backup).State);
    }

    [Fact]
    public void Compare_NeedsTwoBackups_AndNoCar()
    {
        Assert.Equal(CodingFeatureState.NeedsMoreBackups, Status(Inputs(connected: false, backups: 1), CodingFeature.Compare).State);
        Assert.Equal(CodingFeatureState.Available, Status(Inputs(connected: false, backups: 2), CodingFeature.Compare).State);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void ChangingASetting_IsNeverAvailable_WithoutAVerifiedEntryForThisCar(bool connected, bool can, bool pro)
    {
        // Other cars having verified entries (VerifiedOptionsInCatalog) changes nothing for this one.
        var status = Status(Inputs(connected, can, forCar: 0, inCatalog: 25, pro: pro), CodingFeature.ChangeSettings);

        Assert.Equal(CodingFeatureState.NotVerifiedForThisVehicle, status.State);
        Assert.False(status.IsAvailable);
    }

    [Fact]
    public void ChangingASetting_WithAVerifiedEntry_StillNeedsConnectionCanAndPro()
    {
        Assert.Equal(CodingFeatureState.NeedsConnection, Status(Inputs(connected: false, forCar: 1), CodingFeature.ChangeSettings).State);
        Assert.Equal(CodingFeatureState.NotCan, Status(Inputs(can: false, forCar: 1), CodingFeature.ChangeSettings).State);
        Assert.Equal(CodingFeatureState.ProRequired, Status(Inputs(forCar: 1, pro: false), CodingFeature.ChangeSettings).State);
        Assert.Equal(CodingFeatureState.Available, Status(Inputs(forCar: 1), CodingFeature.ChangeSettings).State);
    }

    [Fact]
    public void Restore_NeedsSomethingToRestore_AndAConnection()
    {
        Assert.Equal(CodingFeatureState.NothingToRestore, Status(Inputs(restorable: 0), CodingFeature.RestoreHistory).State);
        Assert.Equal(CodingFeatureState.NeedsConnection, Status(Inputs(connected: false, restorable: 1), CodingFeature.RestoreHistory).State);
        Assert.Equal(CodingFeatureState.Available, Status(Inputs(restorable: 1), CodingFeature.RestoreHistory).State);
    }

    [Fact]
    public void SecurityAccessAndProgramming_AreNeverOffered()
    {
        foreach (var connected in new[] { true, false })
            foreach (var forCar in new[] { 0, 5 })
                Assert.Equal(CodingFeatureState.NotSupported, Status(Inputs(connected, forCar: forCar), CodingFeature.ForbiddenOperations).State);
    }

    [Fact]
    public void EveryFeatureAndState_HasTextInBothLanguages()
    {
        var input = Inputs(forCar: 0, inCatalog: 3, backups: 1);
        foreach (var status in CodingCapabilities.Evaluate(input))
            foreach (var en in new[] { true, false })
            {
                Assert.False(string.IsNullOrWhiteSpace(CodingTexts.Title(status.Feature, en)));
                Assert.False(string.IsNullOrWhiteSpace(CodingTexts.StateText(status.State, en)));
                Assert.False(string.IsNullOrWhiteSpace(CodingTexts.Reason(status, input, en)));
            }

        foreach (var state in Enum.GetValues<CodingFeatureState>())
            Assert.NotEqual(CodingTexts.StateText(state, true), CodingTexts.StateText(state, false));
    }

    [Fact]
    public void NotVerifiedText_StatesTheCatalogCountAndThatNothingIsWritten()
    {
        var input = Inputs(forCar: 0, inCatalog: 0);
        var status = Status(input, CodingFeature.ChangeSettings);

        var text = CodingTexts.Reason(status, input, true);

        Assert.Contains("0 verified", text);
        Assert.Contains("Nothing will be written", text);
    }

    [Fact]
    public void NoTextClaimsSupportThatTheStateDoesNotHave()
    {
        var input = Inputs(forCar: 0, inCatalog: 7);
        var change = Status(input, CodingFeature.ChangeSettings);

        Assert.DoesNotContain("Available", CodingTexts.StateText(change.State, true));
        Assert.Contains("Never", CodingTexts.Reason(Status(input, CodingFeature.ForbiddenOperations), input, true));
    }
}

public sealed class CodingReadinessTests
{
    private static CodingCheck Find(IReadOnlyList<CodingCheck> checks, string key) => checks.Single(check => check.Key == key);

    [Fact]
    public void NotConnected_BlocksOnTheAdapter_AndAsksNothingElse()
    {
        var checks = CodingCapabilities.Readiness(new CodingReadinessInput(false, false, null, null, null, false));

        Assert.Equal(CodingCheckState.Blocked, Find(checks, "adapter").State);
        Assert.DoesNotContain(checks, check => check.Key is "speed" or "volts" or "can" or "identity");
    }

    [Fact]
    public void ANonCanConnection_IsBlocked()
    {
        var checks = CodingCapabilities.Readiness(new CodingReadinessInput(true, false, "1G1RA6E49EU123456", 0, 12.6, true));

        Assert.Equal(CodingCheckState.Blocked, Find(checks, "can").State);
    }

    [Theory]
    [InlineData(null, CodingCheckState.Unknown)]
    [InlineData(0.0, CodingCheckState.Ok)]
    [InlineData(0.4, CodingCheckState.Ok)]
    [InlineData(35.0, CodingCheckState.Blocked)]
    public void Speed_IsJudgedAsTheWritePathJudgesIt(double? speed, CodingCheckState expected)
    {
        var checks = CodingCapabilities.Readiness(new CodingReadinessInput(true, true, "1G1RA6E49EU123456", speed, 12.6, true));

        Assert.Equal(expected, Find(checks, "speed").State);
    }

    [Theory]
    [InlineData(null, CodingCheckState.Unknown)]
    [InlineData(12.2, CodingCheckState.Ok)]
    [InlineData(13.8, CodingCheckState.Ok)]
    [InlineData(11.9, CodingCheckState.Warning)]
    public void Voltage_UsesTheSameLimitAsTheWritePath(double? volts, CodingCheckState expected)
    {
        var checks = CodingCapabilities.Readiness(new CodingReadinessInput(true, true, "1G1RA6E49EU123456", 0, volts, true));

        Assert.Equal(expected, Find(checks, "volts").State);
    }

    [Fact]
    public void AnUnknownVin_IsAWarning_WithTheConsequenceStated()
    {
        var checks = CodingCapabilities.Readiness(new CodingReadinessInput(true, true, null, 0, 12.6, true));

        var vin = Find(checks, "vin");
        Assert.Equal(CodingCheckState.Warning, vin.State);
        Assert.Contains("restoring is blocked", vin.TextEn);
        Assert.Contains("აღდგენა დაბლოკილია", vin.TextKa);
    }

    [Fact]
    public void AnUnidentifiedVehicle_IsAWarning()
    {
        var checks = CodingCapabilities.Readiness(new CodingReadinessInput(true, true, "1G1RA6E49EU123456", 0, 12.6, VehicleIdentified: false));

        Assert.Equal(CodingCheckState.Warning, Find(checks, "identity").State);
    }

    [Fact]
    public void EveryCheck_HasBothLanguages()
    {
        foreach (var checks in new[]
        {
            CodingCapabilities.Readiness(new CodingReadinessInput(false, false, null, null, null, false)),
            CodingCapabilities.Readiness(new CodingReadinessInput(true, true, "1G1RA6E49EU123456", 40, 11.5, true)),
            CodingCapabilities.Readiness(new CodingReadinessInput(true, false, null, null, null, false))
        })
            Assert.All(checks, check =>
            {
                Assert.False(string.IsNullOrWhiteSpace(check.TextKa));
                Assert.False(string.IsNullOrWhiteSpace(check.TextEn));
            });
    }
}

public sealed class CodingVehicleKeyAndBackupStatusTests
{
    [Fact]
    public void AValidVin_IdentifiesTheCar_AndIsCaseInsensitive()
    {
        Assert.Equal("VIN:1G1RA6E49EU123456", CodingCapabilities.VehicleKey("1g1ra6e49eu123456", "x", "y", "2013"));
    }

    [Fact]
    public void WithoutAVin_MakeModelYear_Identify_ButUnknownIsNotAFact()
    {
        Assert.Equal("CAR:CHEVROLET|VOLT|2013", CodingCapabilities.VehicleKey(null, "Chevrolet", "Volt", "2013"));
        Assert.Equal("CAR:CHEVROLET||", CodingCapabilities.VehicleKey("", "Chevrolet", "Unknown", "Unknown"));
        Assert.Equal(string.Empty, CodingCapabilities.VehicleKey(null, "Unknown", "Unknown", "Unknown"));
        Assert.Equal(string.Empty, CodingCapabilities.VehicleKey(null, null, null, null));
    }

    [Fact]
    public void TwoDifferentCars_NeverShareAKey()
    {
        Assert.NotEqual(
            CodingCapabilities.VehicleKey(null, "Ford", "Escape", "2012"),
            CodingCapabilities.VehicleKey(null, "Toyota", "Camry", "2012"));
    }

    private static CodingDidValue Value(string hex = "", string error = "") => new() { Did = "F187", Hex = hex, Error = error };

    [Fact]
    public void AnyReadValue_MakesTheModuleRead()
    {
        Assert.Equal(CodingBackupStatus.Read, CodingBackupStatus.Classify(new[] { Value(error: "NRC 31"), Value("4142") }).Status);
    }

    [Fact]
    public void OnlyForeignReplies_AreForeign_AndNameTheRealSender()
    {
        var (status, note) = CodingBackupStatus.Classify(new[] { Value(error: "reply from another module (0x7E8)"), Value(error: "no answer") });

        Assert.Equal(CodingBackupStatus.Foreign, status);
        Assert.Contains("0x7E8", note);
    }

    [Fact]
    public void OnlyRefusals_AreRefused_AndSilenceIsSilent()
    {
        Assert.Equal((CodingBackupStatus.Refused, "NRC 11"), CodingBackupStatus.Classify(new[] { Value(error: "NRC 11") }));
        Assert.Equal(CodingBackupStatus.Silent, CodingBackupStatus.Classify(new[] { Value(error: "no answer"), Value(error: "timeout") }).Status);
        Assert.Equal(CodingBackupStatus.Silent, CodingBackupStatus.Classify(Array.Empty<CodingDidValue>()).Status);
    }
}

public sealed class CodingCatalogInvariantTests
{
    private static string RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12; i++)
        {
            var candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }

        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void EveryVerifiedOption_CarriesTheEvidenceTheWritePathRequires()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoFile("Resources/Raw/coding_catalog.json")));
        var options = document.RootElement.GetProperty("options").EnumerateArray().ToList();

        foreach (var option in options.Where(o => string.Equals(o.GetProperty("status").GetString(), "verified", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.False(string.IsNullOrWhiteSpace(option.GetProperty("brand").GetString()));
            Assert.True(option.GetProperty("partNumbers").GetArrayLength() > 0, "a verified option must list the module part numbers");
            Assert.True(option.GetProperty("values").GetArrayLength() >= 2, "a verified option needs at least two known values");
            Assert.False(string.IsNullOrWhiteSpace(option.GetProperty("evidence").GetString()), "a verified option must say how it was verified");
            Assert.False(string.IsNullOrWhiteSpace(option.GetProperty("moduleTx").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(option.GetProperty("moduleRx").GetString()));
        }
    }

    [Fact]
    public void TheCodingPathAcceptsOnlyTheFourDocumentedServices()
    {
        var code = File.ReadAllText(RepoFile("Services/OBDService.cs"));
        var start = code.IndexOf("public Task<string> SendCodingRequestAsync(", StringComparison.Ordinal);
        var method = code[start..code.IndexOf("/// <summary>Adapter supply voltage", start, StringComparison.Ordinal)];

        foreach (var service in new[] { "0x10 =>", "0x3E =>", "0x22 =>", "0x2E =>" })
            Assert.Contains(service, method);
        foreach (var forbidden in new[] { "0x27", "0x31", "0x34", "0x36", "0x37", "0x11", "0x14", "0x85", "0x28" })
            Assert.DoesNotContain(forbidden + " =>", method);
        Assert.Contains("_ => false", method);
    }

    [Fact]
    public void TheWritePath_IsGatedByTheVerifiedCatalog_AndAPartNumberAndAReadBack()
    {
        var code = File.ReadAllText(RepoFile("Services/Coding/CodingService.cs"));
        var write = code[code.IndexOf("public async Task<CodingResult> WriteOptionAsync(", StringComparison.Ordinal)..];

        Assert.True(write.IndexOf("OptionsForCurrentCarAsync", StringComparison.Ordinal) < write.IndexOf("WriteBlockAsync", StringComparison.Ordinal));
        Assert.True(write.IndexOf("PartNumbers", StringComparison.Ordinal) < write.IndexOf("WriteBlockAsync", StringComparison.Ordinal));
        Assert.True(write.IndexOf("CreateBackupAsync", StringComparison.Ordinal) < write.IndexOf("WriteBlockAsync(", StringComparison.Ordinal));
    }
}

public sealed class GmlanSweepTests
{
    private static CodingDidValue Read(byte did) => new() { Did = $"1A:{did:X2}", Hex = "0102" };
    private static CodingDidValue Err(byte did, string error) => new() { Did = $"1A:{did:X2}", Error = error };
    private static readonly HashSet<byte> None = new();

    [Fact]
    public async Task OnlyReadValues_AreStored_AndTheKnownOnesAreNotAskedAgain()
    {
        var asked = new List<byte>();
        var known = new HashSet<byte> { 0x90, 0xB0 };

        var result = await GmlanSweep.RunAsync(did =>
        {
            asked.Add(did);
            return Task.FromResult(did is 0x10 or 0xA0 ? Read(did) : Err(did, "NRC 31"));
        }, known, null, CancellationToken.None);

        Assert.DoesNotContain((byte)0x90, asked);
        Assert.DoesNotContain((byte)0xB0, asked);
        Assert.Equal(254, result.Requests);
        Assert.Equal(new[] { "1A:10", "1A:A0" }, result.Values.Select(value => value.Did));
        Assert.Equal(GmlanSweep.StoppedNone, result.Stopped);
    }

    [Fact]
    public async Task ServiceNotSupported_StopsAtOnce()
    {
        var calls = 0;

        var result = await GmlanSweep.RunAsync(did => { calls++; return Task.FromResult(Err(did, "NRC 11")); }, None, null, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(GmlanSweep.StoppedServiceNotSupported, result.Stopped);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task RefusalsOfSingleIdentifiers_AreNormal_AndNeverStopTheSweep()
    {
        var result = await GmlanSweep.RunAsync(did => Task.FromResult(Err(did, "NRC 22")), None, null, CancellationToken.None);

        Assert.Equal(256, result.Requests);
        Assert.Equal(GmlanSweep.StoppedNone, result.Stopped);
    }

    [Fact]
    public async Task ASilentModule_IsAbandonedAfterAFewRequests_NotAfterTwoHundredAndFiftySix()
    {
        var calls = 0;

        var result = await GmlanSweep.RunAsync(did => { calls++; return Task.FromResult(Err(did, "no answer")); }, None, null, CancellationToken.None);

        Assert.Equal(24, calls);
        Assert.Equal(GmlanSweep.StoppedNoAnswers, result.Stopped);
    }

    [Fact]
    public async Task RepliesOfAnotherModule_AreReportedAsSuch()
    {
        var result = await GmlanSweep.RunAsync(did => Task.FromResult(Err(did, "reply from another module (0x7E8)")), None, null, CancellationToken.None);

        Assert.Equal(GmlanSweep.StoppedByForeignReplies, result.Stopped);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task AnAnswer_ResetsTheSilenceCounter()
    {
        // 20 silent, one answer, 20 silent, one answer … never 24 in a row.
        var result = await GmlanSweep.RunAsync(did => Task.FromResult(did % 21 == 20 ? Read(did) : Err(did, "no answer")), None, null, CancellationToken.None);

        Assert.Equal(GmlanSweep.StoppedNone, result.Stopped);
        Assert.NotEmpty(result.Values);
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() => GmlanSweep.RunAsync(did =>
        {
            if (++calls == 3) cts.Cancel();
            return Task.FromResult(Err(did, "NRC 31"));
        }, None, null, cts.Token));
    }

    [Fact]
    public void ForAGmCar_TheBackupExplainsThatIdentificationIsAllItReads_AndPointsToTheExtendedRead()
    {
        var gm = new CodingInputs(true, true, true, 0, 0, 0, 0, true, IsGm: true);
        var other = gm with { IsGm = false };
        var status = CodingCapabilities.Evaluate(gm).Single(item => item.Feature == CodingFeature.Backup);

        var gmText = CodingTexts.Reason(status, gm, true);
        var otherText = CodingTexts.Reason(status, other, true);

        Assert.Contains("Extended read", gmText);
        Assert.Contains("identification blocks only", gmText);
        Assert.DoesNotContain("Extended read", otherText);
        Assert.Contains("გაფართოებული წაკითხვა", CodingTexts.Reason(status, gm, false));
    }
}
