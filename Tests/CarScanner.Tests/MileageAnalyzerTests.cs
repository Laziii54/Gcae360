using CarScanner.Services.Mileage;

namespace CarScanner.Tests;

internal static class MileageTestData
{
    public static MileageEvidence Odo(string ecu, double km, string source = "Test source", string method = "m", MileageSourceKind kind = MileageSourceKind.PublicDefinition,
        string unit = "km", double? raw = null, MileageReadStatus status = MileageReadStatus.Ok) =>
        new(source, ecu, km, 0.8, "test")
        {
            Measurement = MileageMeasurement.TotalOdometer,
            Unit = unit,
            RawValue = raw,
            SourceKind = kind,
            Method = method,
            Status = status
        };

    public static MileageEvidence Other(MileageMeasurement measurement, double value, string unit = "km") =>
        new("Counter", "OBD-II", unit == "km" ? value : double.NaN, 0.5, "test")
        {
            Measurement = measurement,
            Unit = unit,
            RawValue = value,
            SourceKind = MileageSourceKind.StandardObd,
            Method = "01 xx"
        };
}

public sealed class MileageAnalyzerTests
{
    private static MileageAnalysis Analyze(params MileageEvidence[] evidence) => MileageAnalyzer.Analyze(evidence);

    [Fact]
    public void NoEvidence_IsNoReading_AndInventsNothing()
    {
        var analysis = Analyze();
        Assert.Equal(MileageOutcome.NoReading, analysis.Outcome);
        Assert.Null(analysis.Primary);
        Assert.Empty(analysis.Odometer);
        Assert.Null(analysis.SpreadKm);
    }

    [Fact]
    public void OneReading_IsSingleSource_WithNothingToCompare()
    {
        var analysis = Analyze(MileageTestData.Odo("Instrument cluster", 123_456));
        Assert.Equal(MileageOutcome.SingleSource, analysis.Outcome);
        Assert.Equal(123_456, analysis.Primary!.ValueKm);
        Assert.Empty(analysis.Conflicts);
        Assert.Null(analysis.SpreadKm);
    }

    [Fact]
    public void TwoModulesWithinTolerance_AreConsistent()
    {
        var analysis = Analyze(
            MileageTestData.Odo("Instrument cluster", 123_456, "Cluster", "a"),
            MileageTestData.Odo("Engine ECU", 123_460, "Engine", "b"));

        Assert.Equal(MileageOutcome.Consistent, analysis.Outcome);
        Assert.Empty(analysis.Conflicts);
        Assert.Equal(4, analysis.SpreadKm!.Value, 6);
        Assert.NotNull(analysis.Primary);
    }

    [Fact]
    public void TwoModulesFarApart_AreConflicting_AndNoneIsChosen()
    {
        var analysis = Analyze(
            MileageTestData.Odo("Instrument cluster", 123_456, "Cluster", "a"),
            MileageTestData.Odo("Engine ECU", 181_000, "Engine", "b"));

        Assert.Equal(MileageOutcome.Conflicting, analysis.Outcome);
        Assert.Null(analysis.Primary); // neither the highest nor the lowest is promoted
        var conflict = Assert.Single(analysis.Conflicts);
        Assert.Equal(57_544, conflict.DifferenceKm, 6);
        Assert.Equal(2, analysis.Odometer.Count); // both stay visible
    }

    [Fact]
    public void ConflictingReadings_NeverSetTheHeadlineMileage()
    {
        var result = new ActualMileageResult();
        result.Evidence.Add(MileageTestData.Odo("Instrument cluster", 100_000, "Cluster", "a"));
        result.Evidence.Add(MileageTestData.Odo("Engine ECU", 150_000, "Engine", "b"));
        result.ApplyAnalysis();

        Assert.True(result.HasMismatch);
        Assert.Null(result.FinalMileageKm);
        Assert.Equal(50_000, result.MaximumDifferenceKm!.Value, 6);
        Assert.Equal(2, result.SourceCount);
    }

    [Fact]
    public void ConsistentReadings_SetTheHeadline_FromTheMostDirectSource()
    {
        var result = new ActualMileageResult();
        result.Evidence.Add(MileageTestData.Odo("Engine ECU", 100_002, "Engine", "b"));
        result.Evidence.Add(MileageTestData.Odo("Instrument cluster", 100_000, "Cluster", "a"));
        result.ApplyAnalysis();

        Assert.False(result.HasMismatch);
        Assert.Equal(100_000, result.FinalMileageKm);
        Assert.Equal("Instrument cluster", result.Analysis.Primary!.Ecu);
    }

    [Fact]
    public void NothingRead_LeavesEverythingEmpty()
    {
        var result = new ActualMileageResult();
        result.ApplyAnalysis();
        Assert.Null(result.FinalMileageKm);
        Assert.False(result.HasMismatch);
        Assert.Equal(0, result.SourceCount);
        Assert.Equal(string.Empty, result.Details);
    }

    [Theory]
    [InlineData(100_000, 100_100, true)]   // 0.1 % of 100 000 = 100 km → exactly at the limit
    [InlineData(100_000, 100_101, false)]
    [InlineData(1_000, 1_005, true)]       // minimum tolerance 5 km
    [InlineData(1_000, 1_006, false)]
    [InlineData(300_000, 300_250, true)]   // capped at 250 km
    [InlineData(300_000, 300_251, false)]
    public void ToleranceBoundaries(double a, double b, bool consistent)
    {
        var analysis = Analyze(MileageTestData.Odo("A", a, "A", "a"), MileageTestData.Odo("B", b, "B", "b"));
        Assert.Equal(consistent ? MileageOutcome.Consistent : MileageOutcome.Conflicting, analysis.Outcome);
    }

    [Fact]
    public void MilesAndKilometres_AreComparedInTheSameUnit_AndKeepTheirOwnUnit()
    {
        var miles = 62_137.1; // 100 000.0 km
        var analysis = Analyze(
            MileageTestData.Odo("Instrument cluster", miles * MileagePlausibility.KmPerMile, "Cluster", "a", unit: "mi", raw: miles),
            MileageTestData.Odo("Engine ECU", 100_000, "Engine", "b"));

        Assert.Equal(MileageOutcome.Consistent, analysis.Outcome);
        var cluster = analysis.Odometer.Single(e => e.Ecu == "Instrument cluster");
        Assert.Equal("mi", cluster.Unit);
        Assert.Equal(miles, cluster.DisplayValue);
        Assert.Equal(miles * 1.609344, cluster.ValueKm, 6);
    }

    [Fact]
    public void MilesRoundedToWholeUnits_GetAtLeastTwoKilometresOfRoom()
    {
        // 62 137 mi = 99 999.4 km; the other module says 100 000 km — a whole-mile rounding, not a conflict.
        var analysis = Analyze(
            MileageTestData.Odo("Cluster", 62_137 * MileagePlausibility.KmPerMile, "C", "a", unit: "mi", raw: 62_137),
            MileageTestData.Odo("Engine", 100_000, "E", "b"));
        Assert.Equal(MileageOutcome.Consistent, analysis.Outcome);
    }

    [Fact]
    public void KilometresVersusMiles_MixedUp_IsFlaggedAsAConflict()
    {
        // The same digits read as miles on one module and km on another differ by ~61 %.
        var analysis = Analyze(
            MileageTestData.Odo("Cluster", 100_000 * MileagePlausibility.KmPerMile, "C", "a", unit: "mi", raw: 100_000),
            MileageTestData.Odo("Engine", 100_000, "E", "b"));
        Assert.Equal(MileageOutcome.Conflicting, analysis.Outcome);
        Assert.Equal(60_934.4, analysis.Conflicts.Single().DifferenceKm, 1);
    }

    [Fact]
    public void DistanceSinceDtcClear_IsNeverTheOdometer_AndNeverCompared()
    {
        var analysis = Analyze(
            MileageTestData.Odo("OBD-II", 123_456, "A6", "01 A6", MileageSourceKind.StandardObd),
            MileageTestData.Other(MileageMeasurement.DistanceSinceDtcClear, 12),
            MileageTestData.Other(MileageMeasurement.DistanceWithMilOn, 480));

        Assert.Equal(MileageOutcome.SingleSource, analysis.Outcome);
        Assert.Equal(123_456, analysis.Primary!.ValueKm);
        Assert.Equal(2, analysis.Context.Count);
        Assert.Empty(analysis.Conflicts);
        Assert.All(analysis.Odometer, e => Assert.Equal(MileageMeasurement.TotalOdometer, e.Measurement));
    }

    [Fact]
    public void OnlyContextCounters_MeansNoOdometerReading()
    {
        var analysis = Analyze(MileageTestData.Other(MileageMeasurement.DistanceSinceDtcClear, 12_345));
        Assert.Equal(MileageOutcome.NoReading, analysis.Outcome);
        Assert.Null(analysis.Primary);
        Assert.Single(analysis.Context);
    }

    [Fact]
    public void EngineHoursAndMinutes_AreNotDistances_AndNeverConvertedToMileage()
    {
        var hours = MileageTestData.Other(MileageMeasurement.EngineOperatingHours, 2_500, "h");
        var minutes = MileageTestData.Other(MileageMeasurement.MinutesSinceDiagnosticEvent, 900, "min");
        Assert.False(hours.IsDistance);
        Assert.False(minutes.IsDistance);

        var analysis = Analyze(hours, minutes, MileageTestData.Odo("Cluster", 80_000));
        Assert.Equal(MileageOutcome.SingleSource, analysis.Outcome);
        Assert.Equal(80_000, analysis.Primary!.ValueKm);
        Assert.Equal(2, analysis.Context.Count);
        Assert.Equal(2_500, analysis.Context.Single(e => e.Unit == "h").DisplayValue);
    }

    [Fact]
    public void ElectricAndTripDistance_AreContext_NotOdometer()
    {
        var analysis = Analyze(
            MileageTestData.Odo("Cluster", 50_000),
            MileageTestData.Other(MileageMeasurement.ElectricDriveDistance, 31_000),
            MileageTestData.Other(MileageMeasurement.TripDistance, 120),
            MileageTestData.Other(MileageMeasurement.EstimatedDistance, 49_000));

        Assert.Equal(MileageOutcome.SingleSource, analysis.Outcome);
        Assert.Equal(3, analysis.Context.Count);
        Assert.Empty(analysis.Conflicts);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-5d)]
    [InlineData(2_000_001d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ImplausibleOdometerValues_AreFlagged_NotUsed(double km)
    {
        var analysis = Analyze(MileageTestData.Odo("Cluster", km));
        Assert.Equal(MileageOutcome.NoReading, analysis.Outcome);
        var flagged = Assert.Single(analysis.Flagged);
        Assert.Equal(MileageReadStatus.Implausible, flagged.Status);
        Assert.Null(analysis.Primary);
    }

    [Fact]
    public void ImplausibleReading_DoesNotCreateAConflictWithAGoodOne()
    {
        var analysis = Analyze(
            MileageTestData.Odo("Cluster", 90_000, "C", "a"),
            MileageTestData.Odo("Gateway", 4_000_000, "G", "b"));

        Assert.Equal(MileageOutcome.SingleSource, analysis.Outcome);
        Assert.Empty(analysis.Conflicts);
        Assert.Single(analysis.Flagged);
        Assert.Equal(90_000, analysis.Primary!.ValueKm);
    }

    [Theory]
    [InlineData(MileageReadStatus.Malformed)]
    [InlineData(MileageReadStatus.Implausible)]
    public void ReadingsAlreadyMarkedBad_AreNeverUsed(MileageReadStatus status)
    {
        var analysis = Analyze(MileageTestData.Odo("Cluster", 90_000, status: status));
        Assert.Equal(MileageOutcome.NoReading, analysis.Outcome);
        Assert.Single(analysis.Flagged);
    }

    [Fact]
    public void TimeValueLabelledAsOdometer_IsFlagged()
    {
        var wrong = new MileageEvidence("X", "Y", double.NaN, 0.5, "")
        {
            Measurement = MileageMeasurement.TotalOdometer,
            Unit = "h",
            RawValue = 5000
        };
        var analysis = Analyze(wrong);
        Assert.Equal(MileageOutcome.NoReading, analysis.Outcome);
        Assert.Single(analysis.Flagged);
    }

    [Fact]
    public void ExactDuplicates_AreCollapsed_ButTwoMethodsOfTheSameModuleAreKept()
    {
        var duplicate = Analyze(
            MileageTestData.Odo("Cluster", 70_000, "S", "m"),
            MileageTestData.Odo("Cluster", 70_000, "S", "m"));
        Assert.Equal(MileageOutcome.SingleSource, duplicate.Outcome);

        var twoMethods = Analyze(
            MileageTestData.Odo("Cluster", 70_000, "S", "m1"),
            MileageTestData.Odo("Cluster", 70_001, "S", "m2"));
        Assert.Equal(MileageOutcome.Consistent, twoMethods.Outcome);
    }

    [Fact]
    public void ThreeReadings_ReportEveryDisagreeingPair()
    {
        var analysis = Analyze(
            MileageTestData.Odo("A", 100_000, "A", "a"),
            MileageTestData.Odo("B", 100_000, "B", "b"),
            MileageTestData.Odo("C", 140_000, "C", "c"));

        Assert.Equal(MileageOutcome.Conflicting, analysis.Outcome);
        Assert.Equal(2, analysis.Conflicts.Count); // A↔C and B↔C
        Assert.All(analysis.Conflicts, c => Assert.Equal(40_000, c.DifferenceKm, 6));
        Assert.Null(analysis.Primary);
    }

    [Fact]
    public void ResultOrder_DoesNotChangeTheOutcome()
    {
        var a = MileageTestData.Odo("Cluster", 100_000, "C", "a");
        var b = MileageTestData.Odo("Engine", 100_003, "E", "b");
        var c = MileageTestData.Odo("Gateway", 100_001, "G", "c");

        var first = Analyze(a, b, c);
        var second = Analyze(c, b, a);
        Assert.Equal(first.Outcome, second.Outcome);
        Assert.Equal(first.Primary!.Ecu, second.Primary!.Ecu);
        Assert.Equal(first.SpreadKm, second.SpreadKm);
    }

    [Fact]
    public void Primary_PrefersTheCluster_ThenStandardObd_ThenOthers_ThenSavedPlaces()
    {
        var analysis = Analyze(
            MileageTestData.Odo("Saved module", 100_000, "Saved", "s", MileageSourceKind.LearnedOnPhone),
            MileageTestData.Odo("Gateway", 100_001, "Gw", "g"),
            MileageTestData.Odo("OBD-II", 100_002, "A6", "01 A6", MileageSourceKind.StandardObd));
        Assert.Equal("OBD-II", analysis.Primary!.Ecu);

        var withCluster = Analyze(
            MileageTestData.Odo("OBD-II", 100_002, "A6", "01 A6", MileageSourceKind.StandardObd),
            MileageTestData.Odo("Instrument cluster", 100_000, "C", "c"));
        Assert.Equal("Instrument cluster", withCluster.Primary!.Ecu);
    }

    [Fact]
    public void LowerReadingInOneModule_IsNotTreatedAsAnswer()
    {
        // A lower dashboard value is a reason to look, not proof. Neither side is promoted.
        var analysis = Analyze(
            MileageTestData.Odo("Instrument cluster", 80_000, "C", "c"),
            MileageTestData.Odo("Engine ECU", 160_000, "E", "e"));
        Assert.Null(analysis.Primary);
        Assert.Equal(MileageOutcome.Conflicting, analysis.Outcome);
    }
}
