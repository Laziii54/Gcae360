using System.Text.Json;
using CarScanner.Services.Mileage;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Tests;

/// <summary>
/// What OBDb says per model year about the odometer and service definitions (Resources/Raw/obdb_year_evidence.json, derived by
/// research/tools/evidence-obdb.js), and the definitions whose recorded replies show they hold something other than their name.
/// The small cases use invented signal ids; the pinned facts about real vehicles come from OBDb's own filters and recorded test
/// replies. Nothing here ran on a vehicle.
/// </summary>
public sealed class ObdbYearEvidenceTests
{
    private const string Small = """
        {"schema":1,"signals":{
          "BMW-3-Series":{
            "ODO":["2012-2027","2026-2027","2012-2025"],
            "OLD_DEBUG_EVERYWHERE":["*","*",""],
            "ONE_YEAR":["2019","","2019"],
            "SPLIT":["1996-2014, 2024-2027","2027","2024-2026"]},
          "Ford-Mustang":{"LIFE":["*","1996-2015, 2017-2018","2016, 2019"]}}}
        """;

    private static readonly ObdbYearEvidence Evidence = ObdbYearEvidence.Parse(Small);

    // ---- reading and asking ----

    [Fact]
    public void AYearInsideTheFilter_ThatIsNotDebug_IsApplicableAndNotUnderTest()
    {
        var e = Evidence.For("BMW", "3-Series", "ODO", 2019);

        Assert.True(e.Known);
        Assert.True(e.Applicable);
        Assert.False(e.UnderTest);
        Assert.True(e.RecordedReply);
    }

    [Fact]
    public void AYearBeforeTheFilter_IsKnownButNotApplicable()
    {
        var e = Evidence.For("BMW", "3-Series", "ODO", 2011);

        Assert.True(e.Known);
        Assert.False(e.Applicable);
        Assert.False(e.UnderTest);
        Assert.False(e.RecordedReply);
    }

    [Fact]
    public void ADebugYear_IsUnderTest_EvenWhenTheCatalogFlagWasNotSet()
    {
        var e = Evidence.For("BMW", "3-Series", "ODO", 2026);

        Assert.True(e.Applicable);
        Assert.True(e.UnderTest);
        Assert.False(e.RecordedReply);
    }

    [Fact]
    public void AStarInTheDebugColumn_MeansEveryApplicableYear()
    {
        foreach (var year in new[] { 1996, 2005, 2027 })
        {
            var e = Evidence.For("BMW", "3-Series", "OLD_DEBUG_EVERYWHERE", year);
            Assert.True(e.Applicable);
            Assert.True(e.UnderTest);
        }
    }

    [Fact]
    public void ASplitFilter_AppliesToBothEnds_AndNotToTheMiddle()
    {
        Assert.True(Evidence.For("BMW", "3-Series", "SPLIT", 2010).Applicable);
        Assert.True(Evidence.For("BMW", "3-Series", "SPLIT", 2025).Applicable);
        Assert.False(Evidence.For("BMW", "3-Series", "SPLIT", 2018).Applicable);
        Assert.True(Evidence.For("BMW", "3-Series", "SPLIT", 2027).UnderTest);
        Assert.False(Evidence.For("BMW", "3-Series", "SPLIT", 2025).UnderTest);
    }

    [Fact]
    public void ASingleYear_Works()
    {
        Assert.True(Evidence.For("BMW", "3-Series", "ONE_YEAR", 2019).Applicable);
        Assert.False(Evidence.For("BMW", "3-Series", "ONE_YEAR", 2020).Applicable);
    }

    [Fact]
    public void ADefinitionThatIsDebugInSomeYearsAndNotInOthers_IsToldApartByYear()
    {
        Assert.True(Evidence.For("Ford", "Mustang", "LIFE", 2018).UnderTest);
        Assert.False(Evidence.For("Ford", "Mustang", "LIFE", 2019).UnderTest);
        Assert.True(Evidence.For("Ford", "Mustang", "LIFE", 2019).RecordedReply);
        Assert.False(Evidence.For("Ford", "Mustang", "LIFE", 2018).RecordedReply);
    }

    [Fact]
    public void NothingIsConcluded_WhenTheYearTheVehicleOrTheSignalIsUnknown()
    {
        Assert.False(Evidence.For("BMW", "3-Series", "ODO", null).Known);
        Assert.False(Evidence.For("BMW", "3-Series", "ODO", 1980).Known);
        Assert.False(Evidence.For("BMW", "3-Series", "ODO", 2090).Known);
        Assert.False(Evidence.For("BMW", "3-Series", "NOT_THERE", 2019).Known);
        Assert.False(Evidence.For("Audi", "A4", "ODO", 2019).Known);
        Assert.False(Evidence.For(null, "3-Series", "ODO", 2019).Known);
        Assert.False(Evidence.For("BMW", "3-Series", null, 2019).Known);
    }

    [Fact]
    public void TheLookupIgnoresCase()
    {
        Assert.True(Evidence.For("bmw", "3-series", "odo", 2019).Known);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"signals":[1,2]}""")]
    public void AnUnreadableFile_GivesNoEvidence_SoThePagesBehaveAsBefore(string json)
    {
        var evidence = ObdbYearEvidence.Parse(json);

        Assert.Equal(0, evidence.VehicleCount);
        Assert.False(evidence.For("BMW", "3-Series", "ODO", 2019).Known);
    }

    // ---- the real asset ----

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    private static readonly Lazy<ObdbYearEvidence> Real = new(() => ObdbYearEvidence.Parse(File.ReadAllText(RepoFile("Resources", "Raw", "obdb_year_evidence.json"))));

    [Fact]
    public void TheShippedEvidence_ReadsAndHoldsTheVehiclesOfTheCatalog()
    {
        Assert.True(Real.Value.VehicleCount > 400, $"only {Real.Value.VehicleCount} vehicles");
        Assert.True(Real.Value.SignalCount > 3000, $"only {Real.Value.SignalCount} signals");
    }

    [Fact]
    public void ARealFilter_StopsTheBmwOdometerRequestForAnOlderCar()
    {
        // OBDb gives the cluster odometer of the 3 Series a filter from model year 2012; its tests hold replies for 2012-2025.
        var before = Real.Value.For("BMW", "3-Series", "3SERIES_ODO", 2010);
        var inside = Real.Value.For("BMW", "3-Series", "3SERIES_ODO", 2019);

        Assert.True(before.Known);
        Assert.False(before.Applicable);
        Assert.True(inside.Applicable);
        Assert.True(inside.RecordedReply);
        Assert.False(inside.UnderTest);
    }

    [Fact]
    public void ARealDbgFilter_MakesAFordOilLifeDefinitionDebugInTheYearsNobodyRecordedAReply()
    {
        // The catalog says "not debug" for this row in every year. OBDb flags it debug for 2017 and 2018 and has recorded replies for 2016 and 2019.
        Assert.True(Real.Value.For("Ford", "Mustang", "MUSTANG_PCM_ENG_OIL_LIFE_REM", 2018).UnderTest);
        Assert.False(Real.Value.For("Ford", "Mustang", "MUSTANG_PCM_ENG_OIL_LIFE_REM", 2019).UnderTest);
        Assert.True(Real.Value.For("Ford", "Mustang", "MUSTANG_PCM_ENG_OIL_LIFE_REM", 2019).RecordedReply);
        Assert.False(Real.Value.For("Ford", "Mustang", "MUSTANG_PCM_ENG_OIL_LIFE_REM", 2018).RecordedReply);
    }

    [Fact]
    public void EveryEntryOfTheShippedEvidence_IsWellFormed()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoFile("Resources", "Raw", "obdb_year_evidence.json")));
        var years = new System.Text.RegularExpressions.Regex(@"^(\*|(\d{4}(-\d{4})?)(, \d{4}(-\d{4})?)*)?$");

        foreach (var vehicle in document.RootElement.GetProperty("signals").EnumerateObject())
        {
            foreach (var signal in vehicle.Value.EnumerateObject())
            {
                Assert.Equal(3, signal.Value.GetArrayLength());
                foreach (var part in signal.Value.EnumerateArray())
                    Assert.Matches(years, part.GetString() ?? "x");
            }
        }
    }

    // ---- definitions that hold something other than their name ----

    [Theory]
    [InlineData("BMW", 0x22, 0xDFE7, "Distance since last oil change", true)]
    [InlineData("bmw", 0x22, 0xDFE7, "distance since last oil change", true)]
    [InlineData("MINI", 0x22, 0xDFE7, "Distance since last oil change", true)]
    [InlineData("Toyota", 0x22, 0xDFE7, "Distance since last oil change", true)]
    [InlineData("BMW", 0x22, 0xDFE8, "Distance since last oil change", false)]   // another identifier
    [InlineData("BMW", 0x21, 0xDFE7, "Distance since last oil change", false)]   // another service
    [InlineData("BMW", 0x22, 0xDFE7, "Odometer", false)]                          // another signal of the same request
    [InlineData("Ford", 0x22, 0xDFE7, "Distance since last oil change", false)]   // another make
    [InlineData("Toyota", 0x21, 0x41, "Distance since oil change", false)]        // the Toyota definition whose replies do look like a service distance
    public void TheMislabelledList_IsExact(string make, int service, int did, string name, bool expected)
    {
        Assert.Equal(expected, KnownMislabelledDefinitions.TryFind(make, service, did, name, out var evidence));
        Assert.Equal(expected, evidence.Length > 0);
    }

    [Fact]
    public void EverySignalThatFollowsTheOdometerInTheResearch_IsOnTheMislabelledList()
    {
        // research/obdb-evidence-summary.json is produced from the recorded replies. A distance-since / distance-to service definition whose
        // recorded values follow the odometer of the same model year is the odometer; when a new one appears the research says so and this
        // test fails until the list names it.
        using var document = JsonDocument.Parse(File.ReadAllText(RepoFile("research", "obdb-evidence-summary.json")));
        var suspects = document.RootElement.GetProperty("signalsThatFollowTheOdometer");
        Assert.True(suspects.GetArrayLength() > 0, "the research found none: the list would be empty for no reason");

        foreach (var suspect in suspects.EnumerateArray())
        {
            var repo = suspect.GetProperty("repo").GetString()!;
            var make = repo[..repo.IndexOf('-')];
            var request = suspect.GetProperty("request").GetString()!.Split(' ');
            var service = Convert.ToInt32(request[0], 16);
            var did = Convert.ToInt32(request[1], 16);
            var name = suspect.GetProperty("name").GetString();

            Assert.True(KnownMislabelledDefinitions.TryFind(make, service, did, name, out _), $"{repo} {request[0]} {request[1]} '{name}' follows the odometer but is not listed");
        }
    }

    // ---- wiring (source) ----

    private static string Source(params string[] parts) => File.ReadAllText(RepoFile(parts)).Replace("\r\n", "\n");

    [Fact]
    public void TheServiceReader_AsksTheEvidence_ByModelYear_AndSkipsAMislabelledDefinition()
    {
        var source = Source("Services", "OemDiagnostics", "ServiceDataReaderService.cs");

        Assert.Contains("await ObdbYearEvidenceStore.GetAsync(token)", source);
        Assert.Contains("vehicle.Year.IsVerified", source);
        Assert.Contains("yearEvidence.For(row.Make, row.Model, row.ParameterId, modelYear)", source);
        Assert.Contains("if (!evidenceForYear.Applicable)", source);
        Assert.Contains("candidate with { Debug = evidenceForYear.UnderTest, RecordedForYear = evidenceForYear.RecordedReply }", source);
        // both the exact-model rows and the brand-wide fallback rows pass the mislabelled list
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source, @"KnownMislabelledDefinitions\.TryFind\(row\.Make").Count);
    }

    [Fact]
    public void TheMileageProvider_AsksTheEvidence_ByModelYear()
    {
        var source = Source("Services", "Mileage", "CatalogOdometerProvider.cs");

        Assert.Contains("parameter_id AS ParameterId", source);
        Assert.Contains("await ObdbYearEvidenceStore.GetAsync(token)", source);
        Assert.Contains("yearEvidence.For(row.Make, row.Model, row.ParameterId, modelYear)", source);
        Assert.Contains("evidenceForYear.Known && !evidenceForYear.Applicable", source);
        Assert.Contains("evidenceForYear.Known ? evidenceForYear.UnderTest : row.Debug != 0", source);
        Assert.Contains("RecordedReplyForModelYear = signal.RecordedForYear", source);
        // a year the vehicle information has no origin for is not used
        Assert.Contains("vehicle.YearOrigin == VehicleDataOrigin.Unknown ? null : ServiceMatching.ParseYear(vehicle.Year)", source);
    }

    [Fact]
    public void TheMileagePage_SaysWhenARecordedReplyExistsForTheModelYear()
    {
        Assert.False(new MileageEvidence("a", "b", 1, 1, "c").RecordedReplyForModelYear);
        Assert.Contains("model year", MileageWording.RecordedForModelYear(true));
        Assert.False(string.IsNullOrWhiteSpace(MileageWording.RecordedForModelYear(false)));
        Assert.Contains("evidence.RecordedReplyForModelYear ? MileageWording.RecordedForModelYear(en)", Source("ViewModels", "MileageViewModel.cs"));
    }
}
