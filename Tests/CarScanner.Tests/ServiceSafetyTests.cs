using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Services.ServiceProcedures;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// Service procedures and module discovery must never send a state-changing request on their own, and a procedure
/// must never look more capable than the evidence allows. Vehicles: the four real captures (Toyota Camry with an
/// unknown year, Ford Escape 2012, BMW 320i 2018, Chevrolet Volt 2013). Nothing here ran on a vehicle.
/// </summary>
public sealed class ServiceSafetyTests
{
    // ---- classification ----

    [Fact]
    public void EveryRealCatalogProcedure_OnlyShowsSteps()
    {
        var all = ServiceTestData.Real.Procedures;

        Assert.NotEmpty(all);
        Assert.All(all, procedure => Assert.Equal(ServiceEffect.ShowsStepsOnly, ServiceKindClassifier.EffectOf(procedure)));
    }

    [Fact]
    public void AnOemDiagnosticProcedure_IsClassifiedAsChangingState_EvenWithoutAnExecutionPath()
    {
        var automatic = ServiceTestData.Automatic();

        Assert.Equal(ServiceEffect.ChangesVehicleState, ServiceKindClassifier.EffectOf(automatic));
        // Applicable to the car, but with no reviewed execution path it is a guide, not something the app runs.
        var entry = ServiceProcedureCatalogView.Build(new[] { automatic }, ServiceTestData.Facts("Chevrolet", "Volt", "2013")).Single();
        Assert.Equal(ServiceEffect.ChangesVehicleState, entry.Effect);
        Assert.NotEqual(ServiceCompatibilityStatus.Supported, entry.Compatibility.Status);
    }

    [Fact]
    public void ManualAndToolOnlyProcedures_NeverClaimToChangeStateThroughTheApp()
    {
        var manual = ServiceTestData.Manual();
        var toolOnly = ServiceTestData.Manual(steps: 1);
        toolOnly.ToolRequired = true;

        Assert.Equal(ServiceEffect.ShowsStepsOnly, ServiceKindClassifier.EffectOf(manual));
        Assert.Equal(ServiceEffect.ShowsStepsOnly, ServiceKindClassifier.EffectOf(toolOnly));
    }

    // ---- the four captured vehicles ----

    [Theory]
    [InlineData("TOYOTA", "Camry", "Unknown")]
    [InlineData("FORD", "Escape", "2012")]
    [InlineData("BMW", "320i", "2018")]
    [InlineData("CHEVROLET", "Volt", "2013")]
    public void CapturedVehicles_NeverGetAnAppRunProcedure(string make, string model, string year)
    {
        var list = ServiceProcedureCatalogView.Build(ServiceTestData.Real.Procedures, ServiceTestData.Facts(make, model, year));

        Assert.NotEmpty(list);
        Assert.DoesNotContain(list, entry => entry.Compatibility.Status == ServiceCompatibilityStatus.Supported);
        Assert.All(list, entry => Assert.Equal(ServiceEffect.ShowsStepsOnly, entry.Effect));
        Assert.All(list, entry => Assert.NotEqual(ServiceVerification.VehicleVerified, entry.Verification));
    }

    [Fact]
    public void ToyotaWithAnUnknownYear_KeepsItsGuides_ButAsPartial_NotAsAnswers()
    {
        var list = ServiceProcedureCatalogView.Build(ServiceTestData.Real.Procedures, ServiceTestData.Facts("TOYOTA", "Camry", "Unknown"));

        var toyota = list.Where(entry => entry.Definition.Brand.Contains("Toyota", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(toyota);
        Assert.All(toyota, entry =>
        {
            Assert.Equal(ServiceCompatibilityStatus.PartiallySupported, entry.Compatibility.Status);
            Assert.Contains(ServiceReason.YearUnknown, entry.Compatibility.Missing);
        });
    }

    [Fact]
    public void ChevroletVolt2013_GetsOnlyItsOwnGuide_AndTheOwnersManual_AsManualOnly()
    {
        var list = ServiceProcedureCatalogView.Build(ServiceTestData.Real.Procedures, ServiceTestData.Facts("CHEVROLET", "Volt", "2013"));

        Assert.Contains(list, entry => entry.Definition.Id == "chevrolet.volt.2012-2013.oil-life-reset");
        Assert.All(list, entry => Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, entry.Compatibility.Status));
        Assert.DoesNotContain(list, entry => entry.Definition.Brand.Equals("Acura", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ABrandIsNeverTakenFromTheModelOrVin_WhenTheMakeIsUnknown()
    {
        // The user believed these cars were BMW, Chevrolet, Ford and Toyota; the app lists nothing until the brand is known.
        var list = ServiceProcedureCatalogView.Build(ServiceTestData.Real.Procedures, ServiceTestData.Facts("Unknown", "Volt", "2013"));

        Assert.Empty(list);
    }

    // ---- the app never writes by itself ----

    [Fact]
    public void LegacyWritePath_HasNoWritableIdentifier()
    {
        Assert.Empty(OBDService.WritableServiceDids);
    }

    [Fact]
    public async Task ModuleDiscovery_SendsOnlyTroubleCodeReads_OnEveryBranch()
    {
        var sent = new List<string>();
        var allowed = new HashSet<string> { "1902FF", "1802FF00", "A98112" };

        Task<string> Iso(byte[] payload, bool longWait)
        {
            sent.Add(Convert.ToHexString(payload));
            return Task.FromResult("7E8037F1911");          // a refusal sends the probe down every branch
        }

        foreach (var gmlan in new Func<byte[], Task<string>>?[] { null, payload => { sent.Add(Convert.ToHexString(payload)); return Task.FromResult("NO DATA"); } })
        {
            await ModuleDtcProbe.ProbeAsync(Iso, gmlan, 0x7E8, false, null);
            await ModuleDtcProbe.ProbeAsync(Iso, gmlan, 0x73F, false, null);
            await ModuleDtcProbe.ProbeAsync(Iso, gmlan, 0x18DAF110, true, null);
        }

        Assert.NotEmpty(sent);
        Assert.All(sent, request => Assert.Contains(request, allowed));
    }

    [Fact]
    public void TheScanItself_NeverClears_OnlyTheUserInitiatedClearDoes()
    {
        var code = File.ReadAllText(RepoFile("Services/OemDiagnostics/ModuleDtcScanService.cs"));
        var scan = code[code.IndexOf("public async Task<ScanOutcome> ScanAsync(", StringComparison.Ordinal)..];
        scan = scan[..scan.IndexOf("internal static string DescribeProbes", StringComparison.Ordinal)];

        Assert.DoesNotContain("ClearAsync", scan);
        Assert.DoesNotContain("ClearDetailedAsync", scan);
        Assert.DoesNotContain("0x14", scan);

        var probe = File.ReadAllText(RepoFile("Services/OemDiagnostics/ModuleDtcProbe.cs"));
        // the table of negative response CODES at the end legitimately lists 0x14, 0x31 …; only the code that sends is checked
        probe = probe[..probe.IndexOf("ISO 14229 negative response codes", StringComparison.Ordinal)];
        foreach (var forbidden in new[] { "0x14", "0x2E", "0x31", "0x2F", "0x27", "0x10", "0x11" })
            Assert.DoesNotContain(forbidden, probe);
    }

    [Fact]
    public void TheCaptureTap_ReportsOnlyReadServices()
    {
        var code = File.ReadAllText(RepoFile("Services/OBD/Elm327Protocol.cs"));

        // both module paths gate on the same three read services before anything is reported
        Assert.Equal(2, CountOf(code, "payload[0] is 0x19 or 0x21 or 0x22") + CountOf(code, "service is not (0x19 or 0x21 or 0x22)"));
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

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
}
