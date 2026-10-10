using System.Text.Json;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Tests;

/// <summary>
/// The research list of 10 priority vehicles must stay honest: no target is verified, equipment limits are stated,
/// and what the list says about the app is checked against the app's own data files and code.
/// </summary>
public sealed class OemCaptureTargetTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    private static OemCaptureTargetCatalog Load() => OemCaptureTargetCatalog.Parse(Read("Resources/Raw/oem_capture_targets.json"));

    [Fact]
    public void TheTenPriorityVehicles_AreListedInOrder()
    {
        var ids = Load().Targets.Select(t => t.Id).ToArray();
        Assert.Equal(new[]
        {
            "mercedes-c-class", "mercedes-e-class", "nissan-altima", "nissan-rogue", "opel-astra",
            "skoda-octavia", "suzuki-swift", "tesla-model-3", "renault-megane", "chevrolet-volt"
        }, ids);
        Assert.Equal(Enumerable.Range(1, 10), Load().Targets.Select(t => t.Priority));
    }

    [Fact]
    public void TheCatalog_IsConsistent() => Assert.Empty(Load().Validate());

    [Fact]
    public void NoTarget_IsVerified_OrHasARealVehicleCapture()
    {
        foreach (var t in Load().Targets)
        {
            Assert.False(t.Coverage.VerifiedOemDefinition, t.Id);
            Assert.Equal(0, t.Coverage.RealVehicleCaptures);
            Assert.NotEqual(CaptureEvidenceLevel.RealVehicleCaptured, t.Evidence);
        }
    }

    [Fact]
    public void OnlyTheVolt_HasSimulationTests_AndTheTestFilesExist()
    {
        var withTests = Load().Targets.Where(t => t.Coverage.SimulationTested.Count > 0).ToArray();
        var volt = Assert.Single(withTests);
        Assert.Equal("chevrolet-volt", volt.Id);
        Assert.Equal(CaptureEvidenceLevel.SimulationTested, volt.Evidence);
        foreach (var file in volt.Coverage.SimulationTested)
            Assert.True(File.Exists(Path.Combine(Root, file)), file);
    }

    [Fact]
    public void Tesla_IsMarkedAsNeedingOtherEquipment_WithAnExplanation()
    {
        var tesla = Load().Find("tesla-model-3")!;
        Assert.Equal(CaptureAdapterPath.NeedsOtherEquipment, tesla.AdapterPath);
        Assert.Contains("no evidence", tesla.AdapterNotes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Not established", tesla.Coverage.GenericObd2);
    }

    [Theory]
    [InlineData("nissan-altima", CaptureAdapterPath.Elm327ObdPort)]
    [InlineData("nissan-rogue", CaptureAdapterPath.Elm327ObdPort)]
    [InlineData("skoda-octavia", CaptureAdapterPath.Elm327ObdPort)]
    [InlineData("renault-megane", CaptureAdapterPath.Elm327ObdPort)]
    [InlineData("chevrolet-volt", CaptureAdapterPath.Elm327ObdPort)]
    [InlineData("mercedes-c-class", CaptureAdapterPath.Unconfirmed)]
    [InlineData("mercedes-e-class", CaptureAdapterPath.Unconfirmed)]
    [InlineData("opel-astra", CaptureAdapterPath.Unconfirmed)]
    [InlineData("suzuki-swift", CaptureAdapterPath.Unconfirmed)]
    public void AdapterPath_IsAsAssessed(string id, CaptureAdapterPath expected) =>
        Assert.Equal(expected, Load().Find(id)!.AdapterPath);

    [Fact]
    public void ManualServiceProcedures_AreRealCatalogEntries()
    {
        using var doc = JsonDocument.Parse(Read("Resources/Raw/service_procedures.json"));
        var ids = doc.RootElement.GetProperty("procedures").EnumerateArray()
            .Select(p => p.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);

        foreach (var t in Load().Targets)
            foreach (var id in t.Coverage.ManualServiceProcedures)
                Assert.True(ids.Contains(id), $"{t.Id}: {id} is not in service_procedures.json");
    }

    [Fact]
    public void NoTargetBrand_HasAVerifiedDefinition_InTheBundledCatalogs()
    {
        using var defs = JsonDocument.Parse(Read("Resources/Raw/oem_definitions.json"));
        var brands = defs.RootElement.GetProperty("definitions").EnumerateArray()
            .Select(d => d.GetProperty("brand").GetString() ?? "").ToArray();
        var targetMakes = Load().Targets.Select(t => t.Make).ToArray();
        Assert.DoesNotContain(brands, b => targetMakes.Any(m => m.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(m, StringComparison.OrdinalIgnoreCase)));

        using var manufacturers = JsonDocument.Parse(Read("Resources/Raw/manufacturer_definitions.json"));
        foreach (var m in manufacturers.RootElement.GetProperty("manufacturers").EnumerateArray())
        {
            var id = m.GetProperty("brandId").GetString();
            if (id is "mercedes" or "nissan" or "opel" or "skoda" or "suzuki" or "tesla" or "renault" or "chevrolet")
            {
                Assert.Equal("MetadataOnly", m.GetProperty("verificationLevel").GetString());
                Assert.Equal(0, m.GetProperty("vehicleDefinitions").GetArrayLength());
            }
        }
    }

    [Fact]
    public void ModuleScanClaims_MatchTheCode()
    {
        // If the module scan changes, this list must be reviewed: it states which brands get a probe table and
        // which are not recognised at all.
        var code = Read("Services/OemDiagnostics/ModuleDtcScanService.cs");
        Assert.Contains("[BrandFamily.Nissan] = new[]", code);
        Assert.Contains("[BrandFamily.Gm] = new[]", code);
        Assert.Contains("[BrandFamily.VwGroup] = new[]", code);
        Assert.DoesNotContain("[BrandFamily.Renault] = new[]", code);

        var detect = code[code.IndexOf("private static BrandFamily DetectFamily(", StringComparison.Ordinal)..];
        detect = detect[..detect.IndexOf("DetectFamilyFromVin", StringComparison.Ordinal)];
        foreach (var brand in new[] { "MERCEDES", "SUZUKI", "TESLA" })
            Assert.DoesNotContain($"\"{brand}\"", detect);
        var stellantisLine = detect.Split('\n').Single(l => l.Contains("BrandFamily.Stellantis"));
        Assert.Contains("\"OPEL\"", stellantisLine);
        Assert.DoesNotContain("[BrandFamily.Stellantis] = new[]", code);

        var targets = Load();
        Assert.True(targets.Find("nissan-altima")!.Coverage.BrandProbeTable);
        Assert.True(targets.Find("chevrolet-volt")!.Coverage.BrandProbeTable);
        Assert.True(targets.Find("skoda-octavia")!.Coverage.BrandProbeTable);
        Assert.False(targets.Find("opel-astra")!.Coverage.BrandProbeTable);
        Assert.False(targets.Find("renault-megane")!.Coverage.BrandProbeTable);
    }

    [Fact]
    public void EveryTarget_ListsWhatIsMissing_AndTheRealCaptureGap()
    {
        foreach (var t in Load().Targets)
        {
            Assert.NotEmpty(t.Missing);
            Assert.Contains(t.Missing, m => m.Contains("real vehicle", StringComparison.OrdinalIgnoreCase) ||
                                            m.Contains("real Volt", StringComparison.OrdinalIgnoreCase));
            Assert.NotEmpty(t.VehicleConditions);
        }
    }

    [Fact]
    public void ADamagedFile_GivesAnEmptyCatalog_NotAnException()
    {
        Assert.Empty(OemCaptureTargetCatalog.Parse("{ not json").Targets);
        Assert.Empty(OemCaptureTargetCatalog.Parse(null).Targets);
        Assert.Empty(OemCaptureTargetCatalog.Parse("{}").Targets);
    }

    [Fact]
    public void Validate_CatchesOverclaiming()
    {
        var json = Read("Resources/Raw/oem_capture_targets.json")
            .Replace("\"verifiedOemDefinition\": false", "\"verifiedOemDefinition\": true");
        var problems = OemCaptureTargetCatalog.Parse(json).Validate();
        Assert.Contains(problems, p => p.Contains("verified without any real-vehicle capture"));

        var noMissing = OemCaptureTargetCatalog.Parse(
            "{\"targets\":[{\"id\":\"x\",\"priority\":1,\"make\":\"A\",\"model\":\"B\",\"generationsInCatalog\":[],\"powertrain\":\"\"," +
            "\"coverage\":{\"genericObd2\":\"\",\"moduleScanFamily\":\"\",\"brandProbeTable\":false,\"catalogEcuAddresses\":0,\"catalogEnhancedPids\":0," +
            "\"catalogCanSignalFiles\":[],\"verifiedOemDefinition\":false,\"manualServiceProcedures\":[],\"simulationTested\":[],\"realVehicleCaptures\":0,\"notes\":\"\"}," +
            "\"adapterPath\":\"NeedsOtherEquipment\",\"adapterNotes\":\"\",\"missing\":[],\"vehicleConditions\":[]}]}").Validate();
        Assert.Contains(noMissing, p => p.Contains("lists nothing as missing"));
        Assert.Contains(noMissing, p => p.Contains("adapter limitation is not explained"));
    }
}
