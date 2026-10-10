using System.Text.Json;
using CarScanner.Models;
using CarScanner.Services.ServiceProcedures;

namespace CarScanner.Tests;

internal static class ServiceTestData
{
    private static readonly Lazy<ServiceProcedureParseResult> RealCatalog = new(() =>
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12; i++)
        {
            var candidate = Path.Combine(dir, "Resources", "Raw", "service_procedures.json");
            if (File.Exists(candidate))
                return ServiceProcedureParser.Parse(File.ReadAllText(candidate));
            dir = Path.GetDirectoryName(dir) ?? dir;
        }

        throw new FileNotFoundException("service_procedures.json not found");
    });

    public static ServiceProcedureParseResult Real => RealCatalog.Value;

    public static ServiceProcedureDefinition Manual(
        string id = "t.manual", string brand = "Toyota", string model = "Corolla", int min = 2015, int max = 2019,
        string nameEn = "Maintenance Reminder Reset", int steps = 3) =>
        new()
        {
            Id = id, Brand = brand, Model = model, MinimumYear = min, MaximumYear = max, NameEn = nameEn, NameKa = nameEn,
            Category = "Maintenance", ExecutionType = ServiceProcedureExecutionType.Manual, Source = "Owner's manual",
            Steps = Enumerable.Range(1, steps).Select(i => new ServiceProcedureStep { Order = i, TextEn = $"step {i}", TextKa = $"ნაბიჯი {i}" }).ToList()
        };

    public static ServiceProcedureDefinition Automatic(string id = "t.auto", string brand = "Chevrolet", string model = "Volt",
        int min = 2011, int max = 2015, int steps = 2) =>
        new()
        {
            Id = id, Brand = brand, Model = model, MinimumYear = min, MaximumYear = max, NameEn = "Oil Life Reset", NameKa = "ზეთის განულება",
            Category = "Oil Service", ExecutionType = ServiceProcedureExecutionType.OemDiagnostic, OemOperationId = "op.oil",
            RequiredCapabilities = new() { "can-uds" },
            Steps = Enumerable.Range(1, steps).Select(i => new ServiceProcedureStep { Order = i, TextEn = $"step {i}", TextKa = $"ნაბიჯი {i}" }).ToList()
        };

    public static VehicleInfo Car(string make, string model = "Unknown", string year = "Unknown", string engine = "Unknown", string fuel = "Unknown") =>
        new() { Manufacturer = make, Model = model, Year = year, Engine = engine, FuelType = fuel };

    public static ServiceVehicleFacts Facts(string make, string model = "Unknown", string year = "Unknown", string engine = "Unknown",
        string fuel = "Unknown", IEnumerable<string>? capabilities = null) =>
        ServiceVehicleFacts.From(Car(make, model, year, engine, fuel), null, capabilities);
}

public sealed class ServiceProcedureSelectionTests
{
    [Fact]
    public void ExactModelAndYear_SelectsThatProcedure()
    {
        var list = ServiceMatching.SelectForVehicle(ServiceTestData.Real.Procedures, ServiceTestData.Car("Chevrolet", "Volt", "2013"));
        Assert.Contains(list, p => p.Id == "chevrolet.volt.2012-2013.oil-life-reset");
    }

    [Fact]
    public void ModelYearOutsideTheEntry_DoesNotSelectIt()
    {
        var list = ServiceMatching.SelectForVehicle(ServiceTestData.Real.Procedures, ServiceTestData.Car("Chevrolet", "Volt", "2019"));
        Assert.DoesNotContain(list, p => p.Id == "chevrolet.volt.2012-2013.oil-life-reset");
    }

    [Theory]
    [InlineData("Mercedes-Benz")]
    [InlineData("Mercedes")]
    [InlineData("MERCEDES BENZ")]
    public void BrandSpellings_AreEquivalent(string brand)
    {
        var list = ServiceMatching.SelectForVehicle(ServiceTestData.Real.Procedures, ServiceTestData.Car(brand, "E 220", "2010"));
        Assert.Contains(list, p => p.Id == "general.mercedes.2008-2015.call-ok");
    }

    [Fact]
    public void BmwAndAudi_GetTheirGenerationGuides()
    {
        var bmw = ServiceMatching.SelectForVehicle(ServiceTestData.Real.Procedures, ServiceTestData.Car("BMW", "320d", "2012"));
        Assert.Contains(bmw, p => p.Id == "general.bmw.2004-plus.cbs-cluster-button");
        Assert.DoesNotContain(bmw, p => p.Id == "general.bmw.1997-2006.odometer-button");

        var audi = ServiceMatching.SelectForVehicle(ServiceTestData.Real.Procedures, ServiceTestData.Car("Audi", "A4", "2018"));
        Assert.Contains(audi, p => p.Id == "general.audi.2004-plus.zero-or-mmi");
        Assert.Contains(audi, p => p.Id == "general.audi.2016-plus.virtual-cockpit-mmi");
    }

    [Fact]
    public void UnknownYear_KeepsBrandWideEntries_InsteadOfHidingThem()
    {
        var list = ServiceMatching.SelectForVehicle(ServiceTestData.Real.Procedures, ServiceTestData.Car("Mercedes-Benz", "C 200"));
        Assert.True(list.Count(p => p.Id.StartsWith("general.mercedes")) >= 5);
    }

    [Fact]
    public void UnknownBrand_SelectsOnlyTheUniversalEntries()
    {
        var list = ServiceMatching.SelectForVehicle(ServiceTestData.Real.Procedures, ServiceTestData.Car("Unknown"));
        Assert.All(list, p => Assert.Equal("*", p.Brand));
    }

    [Fact]
    public void FallbackBrand_IsUsedWhenTheManufacturerIsUnknown()
    {
        var list = ServiceMatching.SelectForVehicle(ServiceTestData.Real.Procedures, ServiceTestData.Car("Unknown", "Unknown", "2012"), "BMW");
        Assert.Contains(list, p => p.Brand.Contains("BMW"));
    }
}

public sealed class ServiceCompatibilityEvaluatorTests
{
    private static ServiceCompatibility Eval(ServiceProcedureDefinition p, ServiceVehicleFacts f, bool auto = false) =>
        ServiceCompatibilityEvaluator.Evaluate(p, f, auto);

    [Fact]
    public void KnownVehicleMatchingAManualGuide_IsManualOnly_NeverSupported()
    {
        var result = Eval(ServiceTestData.Manual(), ServiceTestData.Facts("Toyota", "Corolla", "2017"));
        Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, result.Status);
        Assert.Equal(ServiceMethod.Manual, result.Method);
        Assert.Contains(ServiceReason.ExactModelMatch, result.Reasons);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public void ManufacturerKnown_ButNothingElse_IsPartial_NotSupported()
    {
        var result = Eval(ServiceTestData.Manual(), ServiceTestData.Facts("Toyota"));
        Assert.Equal(ServiceCompatibilityStatus.PartiallySupported, result.Status);
        Assert.Contains(ServiceReason.ModelUnknown, result.Missing);
        Assert.Contains(ServiceReason.YearUnknown, result.Missing);
    }

    [Fact]
    public void NoManufacturer_IsCompatibilityUnknown()
    {
        var result = Eval(ServiceTestData.Manual(), ServiceTestData.Facts("Unknown"));
        Assert.Equal(ServiceCompatibilityStatus.CompatibilityUnknown, result.Status);
        Assert.Contains(ServiceReason.BrandUnknown, result.Missing);
    }

    [Theory]
    [InlineData("Honda", "Corolla", "2017", ServiceReason.BrandMismatch)]
    [InlineData("Toyota", "Camry", "2017", ServiceReason.ModelMismatch)]
    [InlineData("Toyota", "Corolla", "2012", ServiceReason.YearOutOfRange)]
    [InlineData("Toyota", "Corolla", "2021", ServiceReason.YearOutOfRange)]
    public void KnownFactsThatContradict_ExcludeTheProcedure(string make, string model, string year, ServiceReason why)
    {
        var result = Eval(ServiceTestData.Manual(), ServiceTestData.Facts(make, model, year));
        Assert.Equal(ServiceCompatibilityStatus.NotSupported, result.Status);
        Assert.Contains(why, result.Reasons);
    }

    [Fact]
    public void YearBoundaries_AreInclusive()
    {
        var p = ServiceTestData.Manual();
        Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, Eval(p, ServiceTestData.Facts("Toyota", "Corolla", "2015")).Status);
        Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, Eval(p, ServiceTestData.Facts("Toyota", "Corolla", "2019")).Status);
        Assert.Equal(ServiceCompatibilityStatus.NotSupported, Eval(p, ServiceTestData.Facts("Toyota", "Corolla", "2014")).Status);
    }

    [Fact]
    public void OpenEndedYearRange_AcceptsLaterYears()
    {
        var p = ServiceTestData.Manual(model: "*", min: 2004, max: 0);
        Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, Eval(p, ServiceTestData.Facts("Toyota", "Anything", "2031")).Status);
        Assert.Equal(ServiceCompatibilityStatus.NotSupported, Eval(p, ServiceTestData.Facts("Toyota", "Anything", "2003")).Status);
    }

    [Fact]
    public void BrandWideGuide_IsMarkedAsGenerationLevel()
    {
        var result = Eval(ServiceTestData.Manual(model: "*"), ServiceTestData.Facts("Toyota", "Yaris", "2016"));
        Assert.Contains(ServiceReason.BrandGenerationMatch, result.Reasons);
        Assert.DoesNotContain(ServiceReason.ExactModelMatch, result.Reasons);
    }

    [Fact]
    public void DieselOnlyProcedure_IsExcludedForPetrol_AndPartialWhenFuelIsUnknown()
    {
        var p = ServiceTestData.Manual();
        p.FuelTypes = new() { "Diesel" };
        Assert.Equal(ServiceCompatibilityStatus.NotSupported, Eval(p, ServiceTestData.Facts("Toyota", "Corolla", "2017", fuel: "Gasoline")).Status);
        Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, Eval(p, ServiceTestData.Facts("Toyota", "Corolla", "2017", fuel: "Diesel")).Status);

        var unknown = Eval(p, ServiceTestData.Facts("Toyota", "Corolla", "2017"));
        Assert.Equal(ServiceCompatibilityStatus.PartiallySupported, unknown.Status);
        Assert.Contains(ServiceReason.FuelUnknown, unknown.Missing);
    }

    [Fact]
    public void EngineRestriction_IsHonoured()
    {
        var p = ServiceTestData.Manual();
        p.Engines = new() { "1.9 TDI", "2.0 TDI" };
        Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, Eval(p, ServiceTestData.Facts("Toyota", "Corolla", "2017", engine: "2.0 TDI 110kW")).Status);
        Assert.Equal(ServiceCompatibilityStatus.NotSupported, Eval(p, ServiceTestData.Facts("Toyota", "Corolla", "2017", engine: "1.6 MPI")).Status);
        Assert.Contains(ServiceReason.EngineUnknown, Eval(p, ServiceTestData.Facts("Toyota", "Corolla", "2017")).Missing);
    }

    [Fact]
    public void AutomaticProcedure_IsSupportedOnlyWithARealExecutionPath()
    {
        var p = ServiceTestData.Automatic();
        var facts = ServiceTestData.Facts("Chevrolet", "Volt", "2013", capabilities: new[] { "can-uds" });

        var withPath = Eval(p, facts, auto: true);
        Assert.Equal(ServiceCompatibilityStatus.Supported, withPath.Status);
        Assert.Equal(ServiceMethod.Automatic, withPath.Method);

        var withoutPath = Eval(p, facts, auto: false);
        Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, withoutPath.Status);
        Assert.Contains(ServiceReason.NoAutomaticPath, withoutPath.Reasons);
    }

    [Fact]
    public void AutomaticProcedure_NeedsTheAdapterCapabilities()
    {
        var p = ServiceTestData.Automatic();
        var result = Eval(p, ServiceTestData.Facts("Chevrolet", "Volt", "2013", capabilities: new[] { "k-line" }), auto: true);
        Assert.NotEqual(ServiceCompatibilityStatus.Supported, result.Status);
        Assert.Contains(ServiceReason.AdapterCapabilityMissing, result.Reasons);
    }

    [Fact]
    public void AutomaticProcedureWithoutSteps_AndNoPath_IsNotSupported()
    {
        var p = ServiceTestData.Automatic(steps: 0);
        Assert.Equal(ServiceCompatibilityStatus.NotSupported, Eval(p, ServiceTestData.Facts("Chevrolet", "Volt", "2013"), auto: false).Status);
    }

    [Fact]
    public void IncompleteVehicle_NeverUnlocksAnAutomaticProcedure()
    {
        var p = ServiceTestData.Automatic();
        var result = Eval(p, ServiceTestData.Facts("Chevrolet"), auto: true);
        Assert.Equal(ServiceCompatibilityStatus.PartiallySupported, result.Status);
    }

    [Fact]
    public void ToolOnlyProcedure_IsNotPresentedAsSomethingTheAppCanDo()
    {
        var p = ServiceTestData.Manual(steps: 1);
        p.ToolRequired = true;
        var result = Eval(p, ServiceTestData.Facts("Toyota", "Corolla", "2017"));
        Assert.Equal(ServiceCompatibilityStatus.NotSupported, result.Status);
        Assert.Equal(ServiceMethod.ExternalTool, result.Method);
        Assert.Contains(ServiceReason.ExternalToolRequired, result.Reasons);
    }

    [Fact]
    public void ShortEntryThatSaysDealerOnly_IsDetectedAsExternalTool()
    {
        var p = ServiceTestData.Manual(model: "*", steps: 1);
        p.Steps[0].TextEn = "No owner reset exists; a dealer diagnostic tool is required.";
        Assert.Equal(ServiceMethod.ExternalTool, ServiceKindClassifier.MethodOf(p));
    }

    [Fact]
    public void UniversalEntry_NeedsNoBrand_ButStillNeverBecomesSupported()
    {
        var p = ServiceTestData.Manual(brand: "*", model: "*", min: 0, max: 0);
        var result = Eval(p, ServiceTestData.Facts("Unknown"));
        Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, result.Status);
        Assert.Contains(ServiceReason.UniversalFallback, result.Reasons);
    }
}

public sealed class ServiceProcedureCatalogViewTests
{
    private static readonly IReadOnlyList<ServiceProcedureDefinition> Real = ServiceTestData.Real.Procedures;

    [Fact]
    public void NoManufacturer_ListsNothing_UntilTheUserSearches()
    {
        Assert.False(ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Unknown")).Any());

        var searched = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Unknown"), "assyst");
        Assert.NotEmpty(searched);
        Assert.All(searched, e => Assert.Equal(ServiceCompatibilityStatus.CompatibilityUnknown, e.Compatibility.Status));
    }

    [Fact]
    public void KnownBrandWithoutModelOrYear_StillShowsItsGuides_AsPartial()
    {
        var list = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Mercedes-Benz"));
        Assert.True(list.Count >= 5);
        Assert.All(list, e => Assert.NotEqual(ServiceCompatibilityStatus.Supported, e.Compatibility.Status));
        Assert.Contains(list, e => e.Compatibility.Status == ServiceCompatibilityStatus.PartiallySupported &&
                                   e.Compatibility.Missing.Contains(ServiceReason.YearUnknown));
    }

    [Fact]
    public void FullyKnownMercedes_SeesOnlyItsGenerations()
    {
        var list = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Mercedes-Benz", "C 200", "2016"));
        Assert.Contains(list, e => e.Definition.Id == "general.mercedes.2014-2021.w205-w213");
        Assert.DoesNotContain(list, e => e.Definition.Id == "general.mercedes.1996-2002.zero-button");
        Assert.All(list, e => Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, e.Compatibility.Status));
    }

    [Fact]
    public void ExactEntries_HideBrandWideOnes_ForTheSameBrand()
    {
        var catalog = new[]
        {
            ServiceTestData.Manual("exact", "Toyota", "Corolla", 2015, 2019),
            ServiceTestData.Manual("wide", "Toyota", "*", 2000, 0)
        };
        var list = ServiceProcedureCatalogView.Build(catalog, ServiceTestData.Facts("Toyota", "Corolla", "2017"));
        Assert.Equal(new[] { "exact" }, list.Select(e => e.Definition.Id));
    }

    [Fact]
    public void BrandWideEntry_IsKept_WhenTheModelHasNoExactEntry()
    {
        var catalog = new[]
        {
            ServiceTestData.Manual("exact", "Toyota", "Corolla", 2015, 2019),
            ServiceTestData.Manual("wide", "Toyota", "*", 2000, 0)
        };
        var list = ServiceProcedureCatalogView.Build(catalog, ServiceTestData.Facts("Toyota", "Yaris", "2017"));
        Assert.Equal(new[] { "wide" }, list.Select(e => e.Definition.Id));
    }

    [Fact]
    public void Search_FindsByNameBrandAndSystem_AndMarksForeignEntriesNotSupported()
    {
        var facts = ServiceTestData.Facts("Toyota", "Corolla", "2017");
        var cbs = ServiceProcedureCatalogView.Build(Real, facts, "CBS");
        Assert.Contains(cbs, e => e.Definition.Id.StartsWith("general.bmw") && e.Compatibility.Status == ServiceCompatibilityStatus.NotSupported);

        var audi = ServiceProcedureCatalogView.Build(Real, facts, "audi inspection");
        Assert.All(audi, e => Assert.Contains("audi", ServiceProcedureCatalogView.Normalize(e.Definition.Brand + e.Definition.Id + e.Definition.NameEn + e.Definition.DescriptionEn)));
    }

    [Fact]
    public void Search_IsAccentAndCaseInsensitive()
    {
        var facts = ServiceTestData.Facts("Skoda", "Octavia", "2016");
        Assert.NotEmpty(ServiceProcedureCatalogView.Build(Real, facts, "ŠKODA"));
        Assert.Equal(ServiceProcedureCatalogView.Build(Real, facts, "skoda").Count, ServiceProcedureCatalogView.Build(Real, facts, "ŠKODA").Count);
    }

    [Fact]
    public void Search_WithNoHit_ReturnsEmpty() =>
        Assert.Empty(ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Toyota", "Corolla", "2017"), "zzzz-not-a-procedure"));

    [Fact]
    public void KindFilter_KeepsOnlyThatKind()
    {
        var list = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("BMW", "320d", "2012"), null, ServiceKind.OilService);
        Assert.NotEmpty(list);
        Assert.All(list, e => Assert.Equal(ServiceKind.OilService, e.Kind));
    }

    [Fact]
    public void Ordering_PutsUsableGuidesBeforePartialOnes()
    {
        var catalog = new[]
        {
            ServiceTestData.Manual("partial", "Toyota", "Corolla", 2015, 2019),
            ServiceTestData.Manual("manual", "Toyota", "*", 0, 0)
        };
        var list = ServiceProcedureCatalogView.Build(catalog, ServiceTestData.Facts("Toyota", "Yaris", "2017"));
        Assert.Equal("manual", list[0].Definition.Id);
    }

    [Fact]
    public void AutomaticPathCallback_DecidesWhetherSupportedIsShown()
    {
        var catalog = new[] { ServiceTestData.Automatic() };
        var facts = ServiceTestData.Facts("Chevrolet", "Volt", "2013", capabilities: new[] { "can-uds" });
        Assert.Equal(ServiceCompatibilityStatus.Supported, ServiceProcedureCatalogView.Build(catalog, facts, null, null, _ => true).Single().Compatibility.Status);
        Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, ServiceProcedureCatalogView.Build(catalog, facts, null, null, _ => false).Single().Compatibility.Status);
    }
}

public sealed class ServiceKindTests
{
    private static ServiceKind Kind(string id) =>
        ServiceKindClassifier.KindOf(ServiceTestData.Real.Procedures.Single(p => p.Id == id));

    [Fact]
    public void RealCatalog_KindsAreSensible()
    {
        Assert.Equal(ServiceKind.OilService, Kind("chevrolet.volt.2012-2013.oil-life-reset"));
        Assert.Equal(ServiceKind.MaintenanceReset, Kind("general.mercedes.2014-2021.w205-w213"));
        Assert.Equal(ServiceKind.OilService, Kind("general.bmw.2004-plus.cbs-cluster-button"));
        Assert.Equal(ServiceKind.OilService, Kind("general.audi.2016-plus.virtual-cockpit-mmi"));
    }

    [Fact]
    public void DeclaredKind_WinsOverInference()
    {
        var p = ServiceTestData.Manual(nameEn: "Oil Life Reset");
        p.Kind = "TirePressure";
        Assert.Equal(ServiceKind.TirePressure, ServiceKindClassifier.KindOf(p));
    }

    [Theory]
    [InlineData("TPMS Relearn", ServiceKind.TirePressure)]
    [InlineData("Tire Pressure Monitor Reset", ServiceKind.TirePressure)]
    [InlineData("Battery Registration", ServiceKind.Battery)]
    [InlineData("Electronic Parking Brake Service Mode", ServiceKind.ParkingBrake)]
    [InlineData("Inspection Reset", ServiceKind.Inspection)]
    [InlineData("Engine Oil Life Reset", ServiceKind.OilService)]
    [InlineData("Service Interval Reset", ServiceKind.MaintenanceReset)]
    public void InferredKinds(string name, ServiceKind expected) =>
        Assert.Equal(expected, ServiceKindClassifier.KindOf(ServiceTestData.Manual(nameEn: name)));

    [Fact]
    public void Verification_IsNeverInferredAsVehicleVerified()
    {
        Assert.All(ServiceTestData.Real.Procedures, p =>
            Assert.NotEqual(ServiceVerification.VehicleVerified, ServiceKindClassifier.VerificationOf(p)));

        var noSource = ServiceTestData.Manual();
        noSource.Source = "";
        Assert.Equal(ServiceVerification.Unverified, ServiceKindClassifier.VerificationOf(noSource));

        var declared = ServiceTestData.Manual();
        declared.VerificationStatus = "VehicleVerified";
        Assert.Equal(ServiceVerification.VehicleVerified, ServiceKindClassifier.VerificationOf(declared));
    }
}

public sealed class ServiceProcedureParserTests
{
    private static string Json(params string[] records) =>
        "{ \"schemaVersion\": 1, \"catalogVersion\": 9, \"procedures\": [" + string.Join(",", records) + "] }";

    private const string Good = """
        { "id": "a.b", "brand": "Toyota", "model": "*", "minimumYear": 2010, "maximumYear": 0, "category": "Maintenance",
          "nameKa": "ა", "nameEn": "A", "descriptionKa": "", "descriptionEn": "", "executionType": "Manual", "oemOperationId": "",
          "steps": [ { "order": 1, "textKa": "ა", "textEn": "a" } ], "source": "Manual" }
        """;

    [Fact]
    public void RealCatalog_LoadsCompletely_WithNoExcludedRecord()
    {
        Assert.DoesNotContain(ServiceTestData.Real.Issues, i => i.Excluded);
        Assert.True(ServiceTestData.Real.Procedures.Count >= 150);
        Assert.Equal(ServiceTestData.Real.Procedures.Count, ServiceTestData.Real.Procedures.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void RealCatalog_EveryManualProcedureHasOrderedStepsAndASource()
    {
        Assert.All(ServiceTestData.Real.Procedures, p =>
        {
            Assert.NotEmpty(p.Steps);
            Assert.Equal(Enumerable.Range(1, p.Steps.Count), p.Steps.Select(s => s.Order).OrderBy(o => o));
            Assert.False(string.IsNullOrWhiteSpace(p.Source), p.Id);
        });
    }

    [Fact]
    public void RealCatalog_ContainsNoAutomaticProcedure_BecauseNoOemOperationIsReviewedYet() =>
        Assert.All(ServiceTestData.Real.Procedures, p => Assert.Equal(ServiceProcedureExecutionType.Manual, p.ExecutionType));

    [Fact]
    public void LegacyRecordWithoutNewFields_LoadsWithDefaults()
    {
        var result = ServiceProcedureParser.Parse(Json(Good));
        var p = Assert.Single(result.Procedures);
        Assert.Empty(p.Engines);
        Assert.Empty(p.FuelTypes);
        Assert.Empty(p.Preconditions);
        Assert.False(p.ToolRequired);
        Assert.Equal(9, result.CatalogVersion);
    }

    [Fact]
    public void NewOptionalFields_AreRead()
    {
        var record = Good.Replace("\"source\": \"Manual\"", "\"source\": \"Manual\", \"kind\": \"TirePressure\", \"systemName\": \"TPMS\", \"fuelTypes\": [\"Diesel\"], " +
            "\"engines\": [\"2.0\"], \"requiredCapabilities\": [\"can-uds\"], \"toolRequired\": true, \"verificationStatus\": \"SourceDocumented\", " +
            "\"preconditions\": [ { \"textKa\": \"ა\", \"textEn\": \"Ignition on\" } ]");
        var p = Assert.Single(ServiceProcedureParser.Parse(Json(record)).Procedures);
        Assert.Equal("TirePressure", p.Kind);
        Assert.Equal("TPMS", p.SystemName);
        Assert.Equal(new[] { "Diesel" }, p.FuelTypes);
        Assert.True(p.ToolRequired);
        Assert.Equal("Ignition on", Assert.Single(p.Preconditions).TextEn);
    }

    [Theory]
    [InlineData("\"id\": \"a.b\"", "\"id\": \"\"", "MissingId")]
    [InlineData("\"brand\": \"Toyota\"", "\"brand\": \"\"", "MissingBrand")]
    [InlineData("\"nameKa\": \"ა\", \"nameEn\": \"A\"", "\"nameKa\": \"\", \"nameEn\": \"\"", "MissingName")]
    [InlineData("\"minimumYear\": 2010, \"maximumYear\": 0", "\"minimumYear\": 2020, \"maximumYear\": 2010", "InvalidYearRange")]
    [InlineData("\"minimumYear\": 2010", "\"minimumYear\": -5", "InvalidYearRange")]
    [InlineData("\"steps\": [ { \"order\": 1, \"textKa\": \"ა\", \"textEn\": \"a\" } ]", "\"steps\": []", "ManualWithoutSteps")]
    [InlineData("\"oemOperationId\": \"\"", "\"oemOperationId\": \"op.x\"", "ManualWithOperationId")]
    [InlineData("\"executionType\": \"Manual\"", "\"executionType\": \"OemDiagnostic\"", "OemWithoutOperationId")]
    [InlineData("{ \"order\": 1, \"textKa\": \"ა\", \"textEn\": \"a\" }", "{ \"order\": 1, \"textKa\": \"\", \"textEn\": \"\" }", "EmptyStep")]
    [InlineData("{ \"order\": 1,", "{ \"order\": 0,", "InvalidStepOrder")]
    public void InvalidRecords_AreExcluded_AndReported(string find, string replace, string code)
    {
        var result = ServiceProcedureParser.Parse(Json(Good.Replace(find, replace), Good.Replace("a.b", "second")));

        Assert.Equal("second", Assert.Single(result.Procedures).Id);
        Assert.Contains(result.Issues, i => i.Code == code && i.Excluded);
    }

    [Fact]
    public void DuplicateId_ExcludesTheLaterRecord()
    {
        var result = ServiceProcedureParser.Parse(Json(Good, Good));
        Assert.Single(result.Procedures);
        Assert.Contains(result.Issues, i => i.Code == "DuplicateId");
    }

    [Fact]
    public void UnreadableRecord_DoesNotTakeTheCatalogDown()
    {
        var result = ServiceProcedureParser.Parse(Json(
            Good.Replace("\"executionType\": \"Manual\"", "\"executionType\": \"Teleport\""),
            Good.Replace("a.b", "ok"),
            "42"));
        Assert.Equal("ok", Assert.Single(result.Procedures).Id);
        Assert.Equal(2, result.Issues.Count(i => i.Code == "Unreadable"));
    }

    [Fact]
    public void UnknownKindOrVerification_AreWarnings_NotExclusions()
    {
        var result = ServiceProcedureParser.Parse(Json(Good.Replace("\"source\": \"Manual\"", "\"source\": \"Manual\", \"kind\": \"Weird\", \"verificationStatus\": \"Trust me\"")));
        Assert.Single(result.Procedures);
        Assert.Contains(result.Issues, i => i.Code == "UnknownKind" && !i.Excluded);
        Assert.Contains(result.Issues, i => i.Code == "UnknownVerificationStatus" && !i.Excluded);
    }

    [Fact]
    public void MissingSource_IsAWarning()
    {
        var result = ServiceProcedureParser.Parse(Json(Good.Replace("\"source\": \"Manual\"", "\"source\": \"\"")));
        Assert.Single(result.Procedures);
        Assert.Contains(result.Issues, i => i.Code == "MissingSource" && !i.Excluded);
    }

    [Fact]
    public void WrongSchemaVersion_IsRejected_AsBefore() =>
        Assert.Throws<InvalidOperationException>(() => ServiceProcedureParser.Parse("{ \"schemaVersion\": 2, \"procedures\": [] }"));

    [Fact]
    public void BrokenJson_Throws() =>
        Assert.ThrowsAny<JsonException>(() => ServiceProcedureParser.Parse("{ not json"));

    [Fact]
    public void MissingProcedureList_Throws() =>
        Assert.Throws<InvalidOperationException>(() => ServiceProcedureParser.Parse("{ \"schemaVersion\": 1 }"));
}
