using CarScanner.Api.Services;

namespace CarScanner.Tests;

/// <summary>
/// Display-only module labelling for the admin "ECU მონაცემები" page: a TX/RX pair is named ABS/ECM/… only when
/// the known address table says so for that brand — never guessed, and never used to decide what the app may ask
/// a real vehicle (that stays entirely in the mobile app's own module scan).
/// </summary>
public sealed class ModuleAddressLabelsTests
{
    [Theory]
    [InlineData("CHEVROLET", 0x243, 0x643, "ABS")]
    [InlineData("CHEVROLET", 0x247, 0x647, "SRS")]
    [InlineData("CHEVROLET", 0x241, 0x641, "BCM")]
    [InlineData("GMC", 0x7E4, 0x7EC, "HYB")]
    [InlineData("FORD", 0x760, 0x768, "ABS")]
    [InlineData("TOYOTA", 0x7B0, 0x7B8, "ABS")]
    [InlineData("TOYOTA", 0x791, 0x799, "HYB")]
    [InlineData("VOLKSWAGEN", 0x713, 0x77D, "ABS")]
    [InlineData("HYUNDAI", 0x7D1, 0x7D9, "ABS")]
    public void AKnownAddressOfItsBrand_ResolvesToTheRightModule(string make, int tx, int rx, string expectedId) =>
        Assert.Equal(expectedId, ModuleAddressLabels.ModuleId(make, tx, rx));

    [Fact]
    public void TheStandardEngineAddress_ResolvesForEveryBrand_EvenUnknownOnes()
    {
        Assert.Equal("ECM", ModuleAddressLabels.ModuleId("CHEVROLET", 0x7E0, 0x7E8));
        Assert.Equal("ECM", ModuleAddressLabels.ModuleId("SOME UNKNOWN MAKE", 0x7E0, 0x7E8));
        Assert.Equal("ECM", ModuleAddressLabels.ModuleId(null, 0x7E0, 0x7E8));
    }

    [Fact]
    public void ABrandsAddress_DoesNotLeakIntoAnotherBrand()
    {
        // 0x243/0x643 is GM's ABS address; Ford does not share it.
        Assert.Equal("ABS", ModuleAddressLabels.ModuleId("CHEVROLET", 0x243, 0x643));
        Assert.Null(ModuleAddressLabels.ModuleId("FORD", 0x243, 0x643));
    }

    [Theory]
    [InlineData(0x18DA11F1, 0x18DAF111)]  // the 29-bit extended Volt address seen in a real capture
    [InlineData(0x999, 0x998)]
    public void AnUnknownAddress_ResolvesToNothing_ShownAsUnclassified(int tx, int rx)
    {
        Assert.Null(ModuleAddressLabels.ModuleId("CHEVROLET", tx, rx));
        Assert.Equal(string.Empty, ModuleAddressLabels.Label("CHEVROLET", tx, rx));
    }

    [Fact]
    public void TheGeorgianLabel_IsAReadableName_NotTheRawId()
    {
        Assert.Equal("ABS / მუხრუჭები", ModuleAddressLabels.Label("CHEVROLET", 0x243, 0x643));
        Assert.Equal("ჰიბრიდის სისტემა", ModuleAddressLabels.Label("CHEVROLET", 0x7E4, 0x7EC));
    }
}

/// <summary>
/// The admin "ECU მონაცემები" page: vehicles are grouped by make/model/year, and within a vehicle, by the known
/// module category — never by guesswork. Unclassified data stays visible, not discarded.
/// </summary>
public sealed class AdminEcuObservationsGroupingTests
{
    private static readonly AdminService.Session Session = new("owner@example.test", "csrf-token");

    private static EcuObservationRow Row(string make, string model, string year, int tx, int rx, string request, string response, string review = "unreviewed") =>
        new(1, "2026-10-13", "2026-10-14", make, model, year, "AUTO, ISO 15765-4", tx, rx, false, request, response, 1, 1, 1, "single", review);

    [Fact]
    public void VehiclesAreGrouped_ByMakeModelAndYear()
    {
        var rows = new[]
        {
            Row("CHEVROLET", "VOLT", "2013", 0x7E0, 0x7E8, "0100", "valid"),
            Row("CHEVROLET", "VOLT", "2016", 0x7E0, 0x7E8, "0100", "valid"),
        };

        var html = AdminPages.EcuObservations(Session, rows, null, true);
        Assert.Contains("CHEVROLET VOLT 2013", html);
        Assert.Contains("CHEVROLET VOLT 2016", html);
    }

    [Fact]
    public void AKnownModuleAddress_IsLabelledByCategory_NotJustShownAsHex()
    {
        var rows = new[] { Row("CHEVROLET", "VOLT", "2013", 0x243, 0x643, "1902FF", "NO DATA") };
        var html = AdminPages.EcuObservations(Session, rows, null, true);
        Assert.Contains("ABS / მუხრუჭები", html);
    }

    [Fact]
    public void AnUnknownModuleAddress_IsKeptVisible_UnderUnclassified_NeverGuessed()
    {
        var rows = new[] { Row("CHEVROLET", "VOLT", "2013", 0x999, 0x998, "22ABCD", "7E8037F2231") };
        var html = AdminPages.EcuObservations(Session, rows, null, true);
        Assert.Contains("დაუჯგუფებელი", html);
        Assert.Contains("22ABCD", html);                                      // the raw evidence is still shown
    }

    [Fact]
    public void TheStandardBroadcastAddress_GetsItsOwnCategory_NotUnclassified()
    {
        var rows = new[] { Row("CHEVROLET", "VOLT", "2013", 0x7DF, 0, "0100", "valid;bitmap=BE7FB813") };
        var html = AdminPages.EcuObservations(Session, rows, null, true);
        Assert.Contains("სტანდარტული OBD2", html);
        Assert.DoesNotContain("დაუჯგუფებელი", html);
    }
}

/// <summary>
/// The one admin-only button: server-authoritative, reuses the existing checked backup/extended-read path and
/// the existing consent-gated upload pipeline — nothing parallel was built to move data or to decide who is admin.
/// </summary>
public sealed class DeepScanWiringTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public void NoAdminEmailAddress_IsHardcodedAnywhereInTheMobileApp()
    {
        var root = RepositoryRoot();
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Tests" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "Backend" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(f => File.ReadAllText(f).Contains("zukalazashvili", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(root, f))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AdminStatus_ComesFromTheServer_OnEveryPageAppearance()
    {
        var vm = File.ReadAllText(Path.Combine(RepositoryRoot(), "ViewModels/CodingViewModel.cs"));
        Assert.Contains("IsAdmin = await _account.IsAdminAsync();", vm);
        Assert.Contains("private Task DeepScan() => BackupAsync(extended: true", vm);

        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "Views/CodingPage.xaml"));
        Assert.Contains("IsVisible=\"{Binding IsAdmin}\"", page);
        Assert.Contains("Command=\"{Binding DeepScanCommand}\"", page);
    }

    [Fact]
    public void TheServer_ConfirmsAdminWithTheSameCheckTheAdminPanelUses_NotAHardcodedAddress()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryRoot(), "Services/AccountService.cs"));
        Assert.Contains("IsAdminAsync", service);
        Assert.DoesNotContain("zukalazashvili", service, StringComparison.OrdinalIgnoreCase);

        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "Backend/CarScanner.Api/Program.cs"));
        var first = program.IndexOf("\"/api/accounts/me\"", StringComparison.Ordinal);
        var second = program.IndexOf("\"/api/accounts/me\"", first + 1, StringComparison.Ordinal);
        Assert.True(first > 0 && second > first, "expected both the GET and DELETE /api/accounts/me handlers");
        var get = program[first..second];
        Assert.Contains("AdminAccess.HasPerks(account.AccountId, account.Email)", get);
    }

    [Fact]
    public void TheDeepScan_ReusesTheExistingExtendedReadPath_InsteadOfANewScanEngine()
    {
        var vm = File.ReadAllText(Path.Combine(RepositoryRoot(), "ViewModels/CodingViewModel.cs"));
        // DeepScan() calls the same BackupAsync(extended: true, ...) that CreateExtendedBackup() already uses —
        // one checked module-discovery + identification-read + (GMLAN-only) $1A sweep path, not a parallel one.
        var deepScan = vm.IndexOf("private Task DeepScan()", StringComparison.Ordinal);
        var backupAsync = vm.IndexOf("private async Task BackupAsync(", StringComparison.Ordinal);
        Assert.True(deepScan > 0 && backupAsync > deepScan);
        Assert.Contains("_coding.CreateBackupAsync(note.Trim(), progress, _cts.Token, extended, deepScan: isDeepScan);", vm);
    }

    /// <summary>
    /// The Deep Scan additions (wider $22 sweep on every brand, service/oil-life, freeze frame, Mode 09 extras)
    /// are gated on CreateBackupAsync's own deepScan parameter — never on extendedGmRead alone, so the regular
    /// "Create Extended Backup" button (available to every PRO user, not just admins) keeps its original,
    /// faster, GM-only behavior and does not silently grow much slower for everyone.
    /// </summary>
    [Fact]
    public void TheRegularExtendedBackupButton_IsUnaffectedByDeepScansNewReads()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryRoot(), "Services/Coding/CodingService.cs"));
        var method = service[service.IndexOf("public async Task<CodingBackup> CreateBackupAsync(", StringComparison.Ordinal)..];

        var gmSweep = method.IndexOf("extendedGmRead && item.Status", StringComparison.Ordinal);
        var udsSweep = method.IndexOf("if (deepScan && item.Status", StringComparison.Ordinal);
        var vehicleLevel = method.IndexOf("if (deepScan)", StringComparison.Ordinal);
        Assert.True(gmSweep > 0, "the existing GM-only $1A sweep must still be gated on extendedGmRead");
        Assert.True(udsSweep > gmSweep, "the new $22 sweep must be gated on deepScan, not extendedGmRead");
        Assert.True(vehicleLevel > udsSweep, "service/freeze-frame/Mode 09 reads must also be gated on deepScan");
    }

    /// <summary>
    /// Deep Scan's Mode 09 extras (calibration ID, CVN, ECU name) must never carry the VIN — the same exclusion
    /// ObdExchangeTap itself applies to Mode 09 ("Never true for 04 (clear), 09 (VIN) ...").
    /// </summary>
    [Fact]
    public void DeepScan_NeverReadsTheVinIntoTheGenericMode09List()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryRoot(), "Services/Coding/CodingService.cs"));
        Assert.Contains("_advancedDiagnostic.ReadIdentifiersAsync(includeVin: false, token: token)", service);
    }

    /// <summary>
    /// The freeze-frame and Mode 09 values are local-only (they do not pass through ObdExchangeTap, since Mode 02
    /// and Mode 09 are outside its observable set) — CodingBackup's doc comments must say so, so this is never
    /// mistaken for a server-side gap later.
    /// </summary>
    [Fact]
    public void TheNewBackupFields_AreDocumentedAsLocalOnly()
    {
        var models = File.ReadAllText(Path.Combine(RepositoryRoot(), "Services/Coding/CodingModels.cs"));
        Assert.Contains("ServiceReadings", models);
        Assert.Contains("FreezeFrameValues", models);
        Assert.Contains("Mode09Values", models);
        Assert.Contains("stay local to", models);
    }
}
