using CarScanner.Services.Mileage;

namespace CarScanner.Tests;

public sealed class MileageSourceCompatibilityTests
{
    private static MileageSourceFacts Facts(
        bool manufacturer = true, bool model = true, bool adapter = true, bool can = true,
        int[]? advertised = null, int exact = 0, bool broadcast = false, int learned = 0) =>
        new(manufacturer, model, adapter, can, advertised is null ? null : new HashSet<int>(advertised), exact, broadcast, learned);

    private static MileageSourceAssessment Eval(MileageSourceFamily family, MileageSourceFacts facts, int pid = 0xA6) =>
        MileageSourceCompatibility.Evaluate(family, facts, pid);

    // ---- standard PIDs --------------------------------------------------

    [Fact]
    public void OdometerPid_IsSupported_OnlyWhenTheEcuAdvertisesIt()
    {
        var yes = Eval(MileageSourceFamily.StandardOdometerPid, Facts(advertised: new[] { 0x05, 0xA6 }));
        Assert.Equal(MileageSourceStatus.Supported, yes.Status);
        Assert.Equal(MileageSourceReason.PidAdvertised, yes.Reason);
        Assert.True(yes.ShouldTry);

        var no = Eval(MileageSourceFamily.StandardOdometerPid, Facts(advertised: new[] { 0x05 }));
        Assert.Equal(MileageSourceStatus.Unsupported, no.Status);
        Assert.False(no.ShouldTry);
    }

    [Fact]
    public void OdometerPid_WithoutTheSupportedList_IsUnknown_AndNotTried()
    {
        var result = Eval(MileageSourceFamily.StandardOdometerPid, Facts(advertised: null));
        Assert.Equal(MileageSourceStatus.CompatibilityUnknown, result.Status);
        Assert.Equal(MileageSourceReason.AdvertisedListUnavailable, result.Reason);
        Assert.False(result.ShouldTry);
    }

    [Fact]
    public void AdapterNotReady_BlocksEverySource()
    {
        foreach (var family in Enum.GetValues<MileageSourceFamily>())
        {
            var result = Eval(family, Facts(adapter: false, advertised: new[] { 0xA6 }, exact: 3, broadcast: true, learned: 2));
            Assert.Equal(MileageSourceStatus.CompatibilityUnknown, result.Status);
            Assert.False(result.ShouldTry);
        }
    }

    [Theory]
    [InlineData(0x21)]
    [InlineData(0x31)]
    [InlineData(0x4D)]
    [InlineData(0x4E)]
    public void ContextPids_AreJudgedPerPid(int pid)
    {
        Assert.True(Eval(MileageSourceFamily.StandardContextPid, Facts(advertised: new[] { pid }), pid).ShouldTry);
        Assert.False(Eval(MileageSourceFamily.StandardContextPid, Facts(advertised: new[] { 0x05 }), pid).ShouldTry);
    }

    // ---- catalog UDS ----------------------------------------------------

    [Fact]
    public void CatalogRead_NeedsTheExactModel()
    {
        Assert.Equal(MileageSourceReason.ManufacturerUnknown, Eval(MileageSourceFamily.CatalogUdsRead, Facts(manufacturer: false)).Reason);
        Assert.Equal(MileageSourceReason.ModelUnknown, Eval(MileageSourceFamily.CatalogUdsRead, Facts(model: false, exact: 5)).Reason);
        var none = Eval(MileageSourceFamily.CatalogUdsRead, Facts(exact: 0));
        Assert.Equal(MileageSourceStatus.Unsupported, none.Status);
        Assert.Equal(MileageSourceReason.NoExactModelDefinition, none.Reason);
        Assert.False(none.ShouldTry);
    }

    [Fact]
    public void CatalogRead_WithAnExactDefinition_IsAtMostPartiallySupported()
    {
        var result = Eval(MileageSourceFamily.CatalogUdsRead, Facts(exact: 2));
        Assert.Equal(MileageSourceStatus.PartiallySupported, result.Status);
        Assert.True(result.ShouldTry);
    }

    [Fact]
    public void CatalogRead_NeedsCan()
    {
        var result = Eval(MileageSourceFamily.CatalogUdsRead, Facts(can: false, exact: 2));
        Assert.Equal(MileageSourceStatus.Unsupported, result.Status);
        Assert.Equal(MileageSourceReason.NotCanProtocol, result.Reason);
    }

    // ---- passive CAN + saved places ------------------------------------

    [Fact]
    public void CanBroadcast_IsPartial_WhenThereIsABrandDefinition_AndUnsupportedWithout()
    {
        Assert.Equal(MileageSourceStatus.PartiallySupported, Eval(MileageSourceFamily.CanBroadcast, Facts(broadcast: true)).Status);
        Assert.Equal(MileageSourceStatus.Unsupported, Eval(MileageSourceFamily.CanBroadcast, Facts(broadcast: false)).Status);
        Assert.Equal(MileageSourceStatus.Unsupported, Eval(MileageSourceFamily.CanBroadcast, Facts(can: false, broadcast: true)).Status);
        Assert.Equal(MileageSourceStatus.CompatibilityUnknown, Eval(MileageSourceFamily.CanBroadcast, Facts(manufacturer: false, broadcast: true)).Status);
    }

    [Fact]
    public void SavedPlaces_AreOnlyUsedWhenTheyExist()
    {
        Assert.False(Eval(MileageSourceFamily.LearnedLocation, Facts(learned: 0)).ShouldTry);
        var some = Eval(MileageSourceFamily.LearnedLocation, Facts(learned: 2));
        Assert.Equal(MileageSourceStatus.PartiallySupported, some.Status);
        Assert.True(some.ShouldTry);
    }

    // ---- the rule that matters -----------------------------------------

    [Fact]
    public void NothingIsSupported_BecauseOfTheBrandAlone()
    {
        var flags = new[] { false, true };
        foreach (var family in Enum.GetValues<MileageSourceFamily>())
        foreach (var manufacturer in flags)
        foreach (var model in flags)
        foreach (var can in flags)
        foreach (var broadcast in flags)
        {
            // No supported-PID list, no catalog rows, no saved places: only the brand is known.
            var result = Eval(family, Facts(manufacturer, model, true, can, advertised: null, exact: 0, broadcast: broadcast, learned: 0));
            Assert.NotEqual(MileageSourceStatus.Supported, result.Status);
        }
    }

    [Fact]
    public void SupportedStatus_ExistsOnlyForAdvertisedStandardPids()
    {
        var facts = Facts(advertised: new[] { 0xA6, 0x31 }, exact: 4, broadcast: true, learned: 3);
        foreach (var family in Enum.GetValues<MileageSourceFamily>())
        {
            var result = Eval(family, facts, family == MileageSourceFamily.StandardContextPid ? 0x31 : 0xA6);
            var expectSupported = family is MileageSourceFamily.StandardOdometerPid or MileageSourceFamily.StandardContextPid;
            Assert.Equal(expectSupported, result.Status == MileageSourceStatus.Supported);
        }
    }
}

public sealed class MileageWordingTests
{
    private static readonly bool[] Languages = { true, false };

    [Fact]
    public void EveryValue_HasTextInBothLanguages()
    {
        foreach (var en in Languages)
        {
            foreach (var value in Enum.GetValues<MileageMeasurement>())
            {
                Assert.False(string.IsNullOrWhiteSpace(MileageWording.Measurement(value, en)));
                Assert.False(string.IsNullOrWhiteSpace(MileageWording.MeasurementShort(value, en)));
            }

            foreach (var value in Enum.GetValues<MileageSourceKind>())
                Assert.False(string.IsNullOrWhiteSpace(MileageWording.SourceKind(value, en)));
            foreach (var value in Enum.GetValues<MileageReadStatus>())
                Assert.False(string.IsNullOrWhiteSpace(MileageWording.ReadStatus(value, en)));
            foreach (var value in Enum.GetValues<MileageSourceStatus>())
                Assert.False(string.IsNullOrWhiteSpace(MileageWording.SourceStatus(value, en)));
            foreach (var value in Enum.GetValues<MileageSourceReason>())
                Assert.False(string.IsNullOrWhiteSpace(MileageWording.SourceReason(value, en)));
            foreach (var value in Enum.GetValues<MileageOutcome>())
            {
                Assert.False(string.IsNullOrWhiteSpace(MileageWording.Headline(value, en)));
                Assert.False(string.IsNullOrWhiteSpace(MileageWording.Explanation(value, en)));
            }

            Assert.False(string.IsNullOrWhiteSpace(MileageWording.Disclaimer(en)));
            Assert.False(string.IsNullOrWhiteSpace(MileageWording.ContextNote(en)));
        }
    }

    [Fact]
    public void NoOutcomeText_ClaimsOriginalOrVerifiedMileage_OrAccusesTampering()
    {
        var banned = new[] { "original", "genuine", "verified", "authentic mileage", "rolled back", "rollback", "tampered", "fraud" };
        foreach (var outcome in Enum.GetValues<MileageOutcome>())
        {
            var text = (MileageWording.Headline(outcome, true) + " " + MileageWording.Explanation(outcome, true)).ToLowerInvariant();
            foreach (var word in banned)
            {
                // "unverified" and negations ("does not prove … authentic") are allowed; positive claims are not.
                var cleaned = text.Replace("unverified", string.Empty).Replace("not a finding of tampering", string.Empty);
                Assert.DoesNotContain(word, cleaned);
            }
        }
    }

    [Fact]
    public void Consistent_SaysAgreementDoesNotProveAuthenticity()
    {
        Assert.Contains("does not prove", MileageWording.Explanation(MileageOutcome.Consistent, true));
        Assert.Contains("consistent", MileageWording.Headline(MileageOutcome.Consistent, true), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Conflicting_SaysNoReadingIsChosen_AndItIsNotATamperingFinding()
    {
        var text = MileageWording.Explanation(MileageOutcome.Conflicting, true);
        Assert.Contains("none is chosen as correct", text);
        Assert.Contains("not a finding of tampering", text);
    }

    [Fact]
    public void SingleSource_SaysThereIsNothingToCompare() =>
        Assert.Contains("nothing to compare", MileageWording.Explanation(MileageOutcome.SingleSource, true));

    [Fact]
    public void NoReading_IsNotAFindingAboutTheCar() =>
        Assert.Contains("not a finding", MileageWording.Explanation(MileageOutcome.NoReading, true));

    [Fact]
    public void Disclaimer_SaysDiagnosticDataCannotEstablishHistoricMileage() =>
        Assert.Contains("cannot establish", MileageWording.Disclaimer(true));

    [Fact]
    public void ContextMeasurements_AreNamedAsNotTheOdometer()
    {
        foreach (var measurement in new[]
                 {
                     MileageMeasurement.DistanceSinceDtcClear, MileageMeasurement.DistanceWithMilOn, MileageMeasurement.TripDistance,
                     MileageMeasurement.ElectricDriveDistance, MileageMeasurement.OtherDistance
                 })
            Assert.Contains("not the odometer", MileageWording.Measurement(measurement, true));

        Assert.Contains("not a distance", MileageWording.Measurement(MileageMeasurement.EngineOperatingHours, true));
        Assert.Contains("not a distance", MileageWording.Measurement(MileageMeasurement.MinutesSinceDiagnosticEvent, true));
        Assert.Contains("calculation", MileageWording.Measurement(MileageMeasurement.EstimatedDistance, true));
        Assert.DoesNotContain("not the odometer", MileageWording.Measurement(MileageMeasurement.TotalOdometer, true));
    }

    [Fact]
    public void SourceKinds_NeverImplyRealVehicleVerification()
    {
        Assert.Contains("not tested", MileageWording.SourceKind(MileageSourceKind.PublicDefinition, true));
        Assert.Contains("unverified", MileageWording.SourceKind(MileageSourceKind.LearnedOnPhone, true));
        Assert.Contains("not identified", MileageWording.SourceKind(MileageSourceKind.StandardObd, true));
        Assert.Contains("nothing was requested", MileageWording.SourceKind(MileageSourceKind.CanBroadcast, true));
    }

    [Fact]
    public void BadReadings_AreDescribedAsNotUsed()
    {
        Assert.Contains("not used", MileageWording.ReadStatus(MileageReadStatus.Implausible, true));
        Assert.Contains("not used", MileageWording.ReadStatus(MileageReadStatus.Malformed, true));
    }

    [Theory]
    [InlineData(123456.7, "km", "123 456.7 km")]
    [InlineData(100000.0, "km", "100 000 km")]
    [InlineData(62137.0, "mi", "62 137 mi")]
    public void Values_KeepTheUnitTheModuleReported(double value, string unit, string expected)
    {
        var evidence = new MileageEvidence("s", "e", unit == "mi" ? value * 1.609344 : value, 1, "") { Unit = unit, RawValue = value };
        Assert.Equal(expected, MileageWording.FormatValue(evidence, true));
    }

    [Fact]
    public void HoursAndMinutes_AreFormattedAsTime()
    {
        var hours = MileageTestData.Other(MileageMeasurement.EngineOperatingHours, 2500, "h");
        var minutes = MileageTestData.Other(MileageMeasurement.MinutesSinceDiagnosticEvent, 90, "min");
        Assert.Equal("2 500 h", MileageWording.FormatValue(hours, true));
        Assert.Equal("90 min", MileageWording.FormatValue(minutes, true));
    }

    [Fact]
    public void StatusAndErrorTexts_ForUnsupportedAndUnknown_AreDistinct()
    {
        var texts = Enum.GetValues<MileageSourceStatus>().Select(s => MileageWording.SourceStatus(s, true)).ToList();
        Assert.Equal(texts.Count, texts.Distinct().Count());
        var reasons = Enum.GetValues<MileageSourceReason>().Select(s => MileageWording.SourceReason(s, true)).ToList();
        Assert.Equal(reasons.Count, reasons.Distinct().Count());
    }
}
