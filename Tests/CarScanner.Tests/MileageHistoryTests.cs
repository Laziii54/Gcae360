using CarScanner.Services.Mileage;

namespace CarScanner.Tests;

/// <summary>
/// Multi-module mileage: what kind of distance a signal holds, how history and installation records are kept apart from
/// the current odometer, and what the evidence is allowed to say. Signal names come from the public OBDb catalog (see
/// TestData/obdb-distance-signal-names.txt); the module values in the analyzer tests are invented numbers that exercise the
/// rules, not readings from any vehicle. Nothing here ran on a car.
/// </summary>
public sealed class MileageHistoryTests
{
    // ---- classification of the real vocabulary ----

    private static IReadOnlyList<(int Repos, string Name)> Vocabulary()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "obdb-distance-signal-names.txt");
        return File.ReadAllLines(path)
            .Where(line => line.Length > 0 && line[0] != '#')
            .Select(line => line.Split('\t', 2))
            .Select(parts => (int.Parse(parts[0]), parts[1]))
            .ToArray();
    }

    private static MileageSignalClass Class(string? name) => MileageSignalClassifier.Classify(name, out _);

    private static MileageEventKind Kind(string name)
    {
        MileageSignalClassifier.Classify(name, out var kind);
        return kind;
    }

    [Theory]
    [InlineData("Odometer")]
    [InlineData("Vehicle odometer")]
    [InlineData("Odometer, instrument panel")]
    [InlineData("Odometer, engine computer")]
    [InlineData("Odometer, brake module")]
    [InlineData("Odometer, battery management system")]
    [InlineData("Odometer, power steering module")]
    [InlineData("Odometer, body control module")]
    [InlineData("Odometer (TCM #1)")]
    [InlineData("Transmission odometer")]
    [InlineData("Engine control module odometer")]
    [InlineData("Total distance")]
    [InlineData("Total mileage")]
    [InlineData("Vehicle total distance")]
    [InlineData("Odometer, mileage, HR")]
    public void ACurrentOdometerCopy_StaysATotalOdometer(string name)
    {
        Assert.Equal(MileageSignalClass.TotalOdometer, Class(name));
    }

    [Theory]
    [InlineData("Odometer at last DPF regeneration", MileageEventKind.Regeneration)]
    [InlineData("Diesel particulate filter odometer at last regeneration", MileageEventKind.Regeneration)]
    [InlineData("Odometer at last gasoline particulate filter regeneration", MileageEventKind.Regeneration)]
    [InlineData("Odometer at oxidation catalytic converter replacement", MileageEventKind.Replacement)]
    [InlineData("Odometer at SCR catalytic converter replacement", MileageEventKind.Replacement)]
    [InlineData("Odometer reading at DPF replace", MileageEventKind.Replacement)]
    [InlineData("Odometer reading when changing DPF", MileageEventKind.Replacement)]
    [InlineData("Odometer reading at last adaptation", MileageEventKind.Adaptation)]
    [InlineData("Odometer reading two adaptations ago", MileageEventKind.Adaptation)]
    [InlineData("Odometer, last adaptation", MileageEventKind.Adaptation)]
    [InlineData("Chain elongation outlet, mileage at last adaptation", MileageEventKind.Adaptation)]
    [InlineData("Clutch 1 adaptation kiss point part 1 odometer reading", MileageEventKind.Adaptation)]
    [InlineData("Odometer reading at last warning", MileageEventKind.Warning)]
    [InlineData("Odometer reading at next-to-last warning", MileageEventKind.Warning)]
    [InlineData("Odometer, fourth-to-last warning", MileageEventKind.Warning)]
    [InlineData("Odometer fault LED illuminated", MileageEventKind.Warning)]
    [InlineData("Odometer fault warning light illuminated, OBD", MileageEventKind.Warning)]
    [InlineData("Odometer at last excess speed recognition", MileageEventKind.Warning)]
    [InlineData("Odometer at last overspeed recognition", MileageEventKind.Warning)]
    [InlineData("Odometer at last engine over-speed", MileageEventKind.Warning)]
    [InlineData("Odometer at last service", MileageEventKind.Service)]
    [InlineData("Tire pressure monitor latest deflation odometer", MileageEventKind.TirePressure)]
    [InlineData("Tire pressure monitor deflation odometer history 3", MileageEventKind.TirePressure)]
    [InlineData("Tire pressure monitor set odometer history 9", MileageEventKind.TirePressure)]
    [InlineData("Tire pressure monitoring odometer reading at last adaptation", MileageEventKind.TirePressure)]
    [InlineData("Sensor 1 odometer event 1", MileageEventKind.Other)]
    [InlineData("Sensor #4 odometer event #3", MileageEventKind.Other)]
    [InlineData("Brake disk drying, odometer at last activation", MileageEventKind.Other)]
    [InlineData("Counter steer support, odometer, last activated", MileageEventKind.Other)]
    public void AValueStoredAtAnEvent_IsAHistoricalRecord_NotTheCurrentOdometer(string name, MileageEventKind expected)
    {
        Assert.Equal(MileageSignalClass.OdometerAtEvent, Class(name));
        Assert.Equal(expected, Kind(name));
    }

    [Theory]
    [InlineData("Mileage with ECU installed")]
    [InlineData("Odometer with ECU installed")]
    [InlineData("Odometer with ECU installed, variant 2")]
    public void AnInstallationRecord_IsNotTheCurrentOdometer(string name)
    {
        Assert.Equal(MileageSignalClass.ModuleInstallRecord, Class(name));
    }

    [Theory]
    [InlineData("Mileage of oxidation catalytic converter")]
    [InlineData("Mileage of SCR catalytic converter")]
    [InlineData("Traction battery total mileage")]
    public void AComponentCounter_IsNotTheVehicleOdometer(string name)
    {
        Assert.Equal(MileageSignalClass.ComponentDistance, Class(name));
    }

    [Theory]
    [InlineData("Mileage since last oil change")]
    [InlineData("Mileage since last fault")]
    [InlineData("Mileage since last appearance of fault")]
    [InlineData("Mileage since last diesel particulate filter replacement")]
    [InlineData("Exhaust timing chain extension, mileage since last adaptation")]
    [InlineData("Mileage before servicing")]
    [InlineData("Vehicle mileage remaining before filling the tank with urea solution")]
    [InlineData("Mileage remaining before diesel particulate filter replacement")]
    [InlineData("Average mileage for the last 10 regenerations")]
    [InlineData("Mileage after injector learning")]
    [InlineData("Mileage after injector replacement")]
    [InlineData("Diesel particulate filter regeneration request mileage")]
    [InlineData("Counter steer support, odometer")]
    [InlineData("Diesel particulate filter odometer")]
    [InlineData("DBCAM1 odometer, raw count")]
    public void ADifferenceARemainderAnAverageOrAnAmbiguousCounter_IsNeverUsed(string name)
    {
        Assert.Equal(MileageSignalClass.Excluded, Class(name));
    }

    [Fact]
    public void ANameThatIsNotADistance_IsNotMileage()
    {
        Assert.Equal(MileageSignalClass.NotMileage, Class("Front right tire pressure"));
        Assert.Equal(MileageSignalClass.NotMileage, Class(""));
        Assert.Equal(MileageSignalClass.NotMileage, Class(null));
    }

    [Fact]
    public void EveryNameInTheRealVocabulary_ObeysTheInvariants()
    {
        var vocabulary = Vocabulary();
        Assert.True(vocabulary.Count > 150, "the vocabulary fixture should hold the OBDb distance names");

        string[] deltaWords = { "since", "remaining", "before", "after", "average", "until", "request", "raw count" };
        string[] pastWords = { "event", "history", "adaptation", "warning", "ecu installed", "replace", "changing dpf", "regeneration", "regen", "deflation", "last " };

        foreach (var (_, name) in vocabulary)
        {
            var kind = Class(name);
            var lower = name.ToLowerInvariant();

            // A difference, a remainder or an average is never a snapshot and never the odometer.
            if (deltaWords.Any(lower.Contains))
                Assert.True(kind is MileageSignalClass.Excluded or MileageSignalClass.NotMileage, $"{name} → {kind}");

            // Anything that speaks of an event, a history, an installation or a replacement is never "the current odometer".
            if (pastWords.Any(lower.Contains))
                Assert.NotEqual(MileageSignalClass.TotalOdometer, kind);

            // Only a historical record carries an event kind.
            Assert.Equal(kind == MileageSignalClass.OdometerAtEvent, Kind(name) != MileageEventKind.None);
        }
    }

    [Fact]
    public void TheRealVocabulary_SplitsIntoTheExpectedGroups()
    {
        var counts = Vocabulary().GroupBy(item => Class(item.Name)).ToDictionary(group => group.Key, group => group.Count());

        // Pinned to the 195 names collected on 2026-10-18. A change here means the rules changed: review it, do not just update it.
        Assert.Equal(195, counts.Values.Sum());
        Assert.Equal(72, counts[MileageSignalClass.TotalOdometer]);
        Assert.Equal(90, counts[MileageSignalClass.OdometerAtEvent]);
        Assert.Equal(3, counts[MileageSignalClass.ModuleInstallRecord]);
        Assert.Equal(3, counts[MileageSignalClass.ComponentDistance]);
        Assert.Equal(27, counts[MileageSignalClass.Excluded]);
    }

    // ---- decoding a record the way the catalog describes it ----

    [Fact]
    public void AnInstallationRecord_DecodesLikeAnyOtherDistance_AndZeroOrAllOnesAreNotValues()
    {
        // OBDb Porsche-Macan 22 10A2 "Mileage with ECU installed": 32 bits, ÷1000, kilometers.
        var spec = new OdometerSignalSpec(0, 32, 1, 1000, 0, false, false);

        var value = OdometerResponseDecoder.Decode(spec, new byte[] { 0x07, 0x5B, 0xCD, 0x15 });   // 123 456 789 / 1000
        Assert.True(value.IsValue);
        Assert.Equal(123456.789, value.Km!.Value, 3);
        Assert.Equal("km", value.Unit);

        Assert.Equal(OdometerDecodeFailure.NotAvailable, OdometerResponseDecoder.Decode(spec, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }).Failure);

        var zero = OdometerResponseDecoder.Decode(spec, new byte[] { 0, 0, 0, 0 });
        Assert.False(zero.IsValue);      // "nothing recorded"
        Assert.Equal(0d, zero.Km);
    }

    // ---- the analyzer ----

    private static MileageEvidence Total(string ecu, double km, string method = "0x22 0x2002 @ 714") =>
        new("Multi-module odometer", ecu, km, 0.8, "current") { Method = method };

    private static MileageEvidence Record(string ecu, double km, MileageEventKind kind, string signal, string method = "0x22 0x3001 @ 7E0") =>
        new("Multi-module odometer", ecu, km, 0.8, signal)
        {
            Measurement = MileageMeasurement.OdometerAtEvent,
            EventKind = kind,
            SignalName = signal,
            Method = method,
            RawHex = "0001E240"
        };

    private static MileageEvidence Install(string ecu, double km, string signal = "Mileage with ECU installed") =>
        new("Multi-module odometer", ecu, km, 0.8, signal)
        {
            Measurement = MileageMeasurement.ModuleInstallRecord,
            SignalName = signal,
            Method = "0x22 0x10A2 @ 7E0"
        };

    private static MileageFinding? Find(MileageAnalysis analysis, MileageFindingCode code) =>
        analysis.Findings.FirstOrDefault(f => f.Code == code);

    [Fact]
    public void RecordsBelowTheCurrentOdometer_AreReportedAsNotAbove_AndNeverAsProof()
    {
        var analysis = MileageAnalyzer.Analyze(new[]
        {
            Total("Instrument cluster", 120_000),
            Record("Engine ECU", 80_000, MileageEventKind.Regeneration, "Odometer at last DPF regeneration"),
            Record("Engine ECU", 95_000, MileageEventKind.Adaptation, "Odometer reading at last adaptation")
        });

        Assert.NotNull(Find(analysis, MileageFindingCode.HistoryNotAboveCurrent));
        Assert.Null(Find(analysis, MileageFindingCode.HistoricalExceedsCurrent));
        Assert.Equal(2, analysis.History.Count);
        Assert.Equal(95_000, analysis.HighestRecorded!.ValueKm);
        Assert.DoesNotContain(analysis.Findings, f => f.Level == MileageFindingLevel.Attention);
    }

    [Fact]
    public void ARecordAboveEvenTheHighestCurrentReading_IsAnObservationWithItsDifference_NotAnAccusation()
    {
        var analysis = MileageAnalyzer.Analyze(new[]
        {
            Total("Instrument cluster", 100_000),
            Record("Brake module", 150_000, MileageEventKind.Warning, "Odometer reading at last warning")
        });

        var finding = Find(analysis, MileageFindingCode.HistoricalExceedsCurrent);
        Assert.NotNull(finding);
        Assert.Equal(MileageFindingLevel.Attention, finding!.Level);
        Assert.Equal(50_000, finding.DifferenceKm!.Value, 3);
        Assert.Contains(finding.Evidence, e => e.Measurement == MileageMeasurement.OdometerAtEvent);
        Assert.Contains(finding.Evidence, e => e.Measurement == MileageMeasurement.TotalOdometer);
        // The record never becomes "the mileage".
        Assert.Equal(100_000, analysis.Primary!.ValueKm);
        Assert.Equal(MileageOutcome.SingleSource, analysis.Outcome);
    }

    [Fact]
    public void ALowReadingModule_CannotCreateTheFinding_OnlyTheHighestCurrentReadingCounts()
    {
        // Cluster 100 000, engine ECU 160 000: they differ (a finding of its own). A record of 150 000 is below the highest
        // current reading, so it is not "above the current odometer".
        var analysis = MileageAnalyzer.Analyze(new[]
        {
            Total("Instrument cluster", 100_000),
            Total("Engine ECU", 160_000, "0x22 0x1111 @ 7E0"),
            Record("Brake module", 150_000, MileageEventKind.Warning, "Odometer reading at last warning")
        });

        Assert.NotNull(Find(analysis, MileageFindingCode.SourcesDiffer));
        Assert.Null(Find(analysis, MileageFindingCode.HistoricalExceedsCurrent));
        Assert.Equal(MileageOutcome.Conflicting, analysis.Outcome);
    }

    [Fact]
    public void ARecordWithinTheToleranceOfTheCurrentReading_IsNotAboveIt()
    {
        var analysis = MileageAnalyzer.Analyze(new[]
        {
            Total("Instrument cluster", 100_000),
            Record("Engine ECU", 100_040, MileageEventKind.Service, "Odometer at last service")   // 0.1 % of 100 000 = 100 km tolerance
        });

        Assert.Null(Find(analysis, MileageFindingCode.HistoricalExceedsCurrent));
        Assert.NotNull(Find(analysis, MileageFindingCode.HistoryNotAboveCurrent));
    }

    [Fact]
    public void AnInstallationRecordAboveTheCurrentOdometer_IsItsOwnFinding_AndSaysNothingAboutWhichMeaningItHas()
    {
        var analysis = MileageAnalyzer.Analyze(new[]
        {
            Total("Instrument cluster", 60_000),
            Install("Engine ECU", 130_000)
        });

        var finding = Find(analysis, MileageFindingCode.ModuleRecordExceedsCurrent);
        Assert.NotNull(finding);
        Assert.Equal(70_000, finding!.DifferenceKm!.Value, 3);
        Assert.Single(analysis.InstallRecords);
        Assert.Empty(analysis.History);
    }

    [Fact]
    public void AnInstallationRecordFarBelowTheOdometer_IsNeverAConflict()
    {
        // Normal for a module that was installed later, and for a counter that starts at installation: it must not turn a
        // consistent odometer into "readings differ".
        var analysis = MileageAnalyzer.Analyze(new[]
        {
            Total("Instrument cluster", 120_000),
            Total("Engine ECU", 120_003, "0x22 0x1111 @ 7E0"),
            Install("Engine ECU", 4_200)
        });

        Assert.Equal(MileageOutcome.Consistent, analysis.Outcome);
        Assert.Empty(analysis.Conflicts);
        Assert.Null(Find(analysis, MileageFindingCode.ModuleRecordExceedsCurrent));
        Assert.NotNull(Find(analysis, MileageFindingCode.SourcesAgree));
    }

    [Fact]
    public void RecordsWithoutACurrentOdometer_AreKept_ButCannotBeComparedWithOne()
    {
        var analysis = MileageAnalyzer.Analyze(new[]
        {
            Record("Engine ECU", 90_000, MileageEventKind.Adaptation, "Odometer reading at last adaptation")
        });

        Assert.Equal(MileageOutcome.NoReading, analysis.Outcome);
        Assert.Single(analysis.History);
        Assert.NotNull(Find(analysis, MileageFindingCode.NoCurrentOdometer));
        Assert.Null(analysis.Primary);
    }

    [Fact]
    public void OneCurrentReadingAndNoRecords_SaysThereIsNothingToCompareWith()
    {
        var analysis = MileageAnalyzer.Analyze(new[] { Total("Instrument cluster", 50_000) });

        Assert.NotNull(Find(analysis, MileageFindingCode.SingleSourceNoHistory));
        Assert.Empty(analysis.History);
    }

    [Fact]
    public void RecordsNeverChangeTheCurrentOdometerOutcome_OrTheHeadline()
    {
        var withoutRecords = MileageAnalyzer.Analyze(new[] { Total("Instrument cluster", 120_000), Total("Engine ECU", 120_002, "0x22 0x1111 @ 7E0") });
        var withRecords = MileageAnalyzer.Analyze(new[]
        {
            Total("Instrument cluster", 120_000),
            Total("Engine ECU", 120_002, "0x22 0x1111 @ 7E0"),
            Record("Engine ECU", 900_000, MileageEventKind.Other, "Sensor 1 odometer event 1"),
            Install("Engine ECU", 1_000_000)
        });

        Assert.Equal(withoutRecords.Outcome, withRecords.Outcome);
        Assert.Equal(withoutRecords.Primary!.ValueKm, withRecords.Primary!.ValueKm);
        Assert.Equal(2, withRecords.Odometer.Count);
        Assert.Empty(withRecords.Conflicts);
    }

    [Fact]
    public void AZeroRecord_MeansNothingWasRecorded_AndIsFlaggedNotUsed()
    {
        var analysis = MileageAnalyzer.Analyze(new[]
        {
            Total("Instrument cluster", 100_000),
            Record("Engine ECU", 0, MileageEventKind.Adaptation, "Odometer reading at last adaptation")
        });

        Assert.Empty(analysis.History);
        Assert.Single(analysis.Flagged);
        Assert.Equal(MileageReadStatus.Implausible, analysis.Flagged[0].Status);
    }

    [Fact]
    public void RecordsAndInstallationsAreNotContext_AndOtherDistancesStayContext()
    {
        var other = new MileageEvidence("OBD-II", "OBD-II", 321, 0.9, "since clear") { Measurement = MileageMeasurement.DistanceSinceDtcClear };
        var analysis = MileageAnalyzer.Analyze(new[]
        {
            Total("Instrument cluster", 100_000),
            Record("Engine ECU", 50_000, MileageEventKind.Service, "Odometer at last service"),
            Install("Engine ECU", 4_000),
            other
        });

        Assert.Single(analysis.Context);
        Assert.Equal(MileageMeasurement.DistanceSinceDtcClear, analysis.Context[0].Measurement);
    }

    [Fact]
    public void ADuplicateRecord_IsCountedOnce()
    {
        var record = Record("Engine ECU", 80_000, MileageEventKind.Regeneration, "Odometer at last DPF regeneration");
        var analysis = MileageAnalyzer.Analyze(new[] { Total("Instrument cluster", 100_000), record, record });

        Assert.Single(analysis.History);
    }

    [Fact]
    public void ComparabilityGroups_KeepDifferentMeaningsApart()
    {
        Assert.Equal("odometer", Total("A", 1).ComparabilityGroup);
        Assert.Equal("OdometerAtEvent", Record("A", 1, MileageEventKind.Other, "x").ComparabilityGroup);
        Assert.Equal("ModuleInstallRecord", Install("A", 1).ComparabilityGroup);
        Assert.Equal("DistanceSinceDtcClear", new MileageEvidence("a", "b", 1, 1, "c") { Measurement = MileageMeasurement.DistanceSinceDtcClear }.ComparabilityGroup);
    }

    [Fact]
    public void RawResponseAndDefinitionName_AreKeptOnTheEvidence()
    {
        var record = Record("Engine ECU", 80_000, MileageEventKind.Regeneration, "Odometer at last DPF regeneration");

        Assert.Equal("0001E240", record.RawHex);
        Assert.Equal("Odometer at last DPF regeneration", record.SignalName);
        Assert.Equal(MileageEventKind.Regeneration, record.EventKind);
    }

    // ---- wording ----

    [Fact]
    public void EveryFinding_HasATitleAndATextInBothLanguages_AndNoneAccusesOrNamesAnOriginalMileage()
    {
        foreach (var code in Enum.GetValues<MileageFindingCode>())
        {
            foreach (var english in new[] { true, false })
            {
                var title = MileageWording.FindingTitle(code, english);
                var text = MileageWording.FindingText(code, english, "50 000 km");
                Assert.False(string.IsNullOrWhiteSpace(title));
                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.DoesNotContain("original mileage", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("rolled back", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("odometer fraud", text, StringComparison.OrdinalIgnoreCase);
            }
        }

        // The two "exceeds" texts put the amount in, and say plainly that they are not a verdict.
        var exceeds = MileageWording.FindingText(MileageFindingCode.HistoricalExceedsCurrent, true, "50 000 km");
        Assert.Contains("50 000 km", exceeds);
        Assert.Contains("not a finding of tampering", exceeds);
        Assert.Contains("50 000 km", MileageWording.FindingText(MileageFindingCode.ModuleRecordExceedsCurrent, true, "50 000 km"));
        Assert.Contains("50 000 km", MileageWording.FindingText(MileageFindingCode.HistoricalExceedsCurrent, false, "50 000 km"));
    }

    [Fact]
    public void TheNotAboveText_StatesThatItDoesNotProveAuthenticity()
    {
        var text = MileageWording.FindingText(MileageFindingCode.HistoryNotAboveCurrent, true);

        Assert.Contains("does not prove", text);
        Assert.Contains("authentic", text);
    }

    [Fact]
    public void EveryEventKindAndMeasurement_HasWording()
    {
        foreach (var kind in Enum.GetValues<MileageEventKind>())
            Assert.False(string.IsNullOrWhiteSpace(MileageWording.EventKind(kind, true)));

        Assert.Contains("past event", MileageWording.Measurement(MileageMeasurement.OdometerAtEvent, true));
        Assert.Contains("not the current odometer", MileageWording.Measurement(MileageMeasurement.ModuleInstallRecord, true));
        Assert.NotEqual(MileageWording.MeasurementShort(MileageMeasurement.OdometerAtEvent, true), MileageWording.MeasurementShort(MileageMeasurement.TotalOdometer, true));
    }

    // ---- wiring of the provider (source) ----

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray())).Replace("\r\n", "\n");
    }

    [Fact]
    public void TheProvider_ReadsRecordsAfterTheCurrentValues_BoundedAndOnlyForTheFullScan()
    {
        var source = Read("Services", "Mileage", "CatalogOdometerProvider.cs");

        Assert.Contains("MileageSignalClassifier.Classify(row.Name, out var eventKind)", source);
        Assert.Contains(".Where(signal => signal.Class == MileageSignalClass.TotalOdometer)", source);
        Assert.Contains("if (!stopAfterFirst)\n            await ReadRecordsAsync(", source);
        Assert.Contains("private const int MaximumRecordRequests = 10;", source);

        // Read-only, exactly like the current-value pass: the same request path, no write, no session change.
        Assert.Contains("_obd.ReadPhysicalCanReadOnlyAsync(", source);
        Assert.DoesNotContain("Write", source.Replace("ReadOnly", string.Empty), StringComparison.Ordinal);
        Assert.Contains("RawHex = data is null", source);
    }

    [Fact]
    public void ADefinitionTheCatalogStillFlagsAsUnderTest_IsSaidSoOnTheReading()
    {
        Assert.False(new MileageEvidence("a", "b", 1, 1, "c").DefinitionUnderTest);
        Assert.True(new MileageEvidence("a", "b", 1, 1, "c") { DefinitionUnderTest = true }.DefinitionUnderTest);
        Assert.Contains("under test", MileageWording.DefinitionUnderTest(true));
        Assert.False(string.IsNullOrWhiteSpace(MileageWording.DefinitionUnderTest(false)));

        // The flag travels from the catalog row to the line shown under the reading, on both lists.
        var provider = Read("Services", "Mileage", "CatalogOdometerProvider.cs");
        Assert.Contains("debug AS Debug", provider);
        Assert.Contains("row.Debug != 0", provider);
        Assert.Contains("DefinitionUnderTest = signal.UnderTest", provider);
        Assert.Contains("evidence.DefinitionUnderTest ? MileageWording.DefinitionUnderTest(en) : string.Empty", Read("ViewModels", "MileageViewModel.cs"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(Read("Views", "MileagePage.xaml"), "Text=\"\\{Binding DefinitionText\\}\"").Count);
    }

    [Fact]
    public void TheServicePageReader_StillStopsAtTheFirstCurrentValue_WithNoRecordPass()
    {
        var source = Read("Services", "Mileage", "CatalogOdometerProvider.cs");

        Assert.Contains("(await ReadCoreAsync(vehicle, true, cancellationToken).ConfigureAwait(false)).FirstOrDefault(e => e.Status == MileageReadStatus.Ok)", source);
        Assert.Contains("if (decodedAny && stopAfterFirst)\n                break;", source);
    }
}
