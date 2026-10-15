using System.Text.RegularExpressions;
using CarScanner.Controls;
using CarScanner.Models;
using CarScanner.Services;

namespace CarScanner.Tests;

/// <summary>
/// A saved brand or an earlier trip must never read as the car that is connected now. The session logic
/// (<see cref="VehicleSessionIdentity"/>) and the card texts (<see cref="VehicleIdentityText"/>) are what the app runs; the
/// wiring in the view model and the service is checked by reading their source. Nothing here talks to a real adapter.
/// </summary>
public sealed class VehicleSessionIdentityTests
{
    private static string Root
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "CarScanner.csproj")))
                    return dir;
                dir = Path.GetDirectoryName(dir) ?? dir;
            }

            throw new DirectoryNotFoundException("repository root not found");
        }
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root }.Concat(parts).ToArray())).Replace("\r\n", "\n");

    private static VehicleInfo Car(string make, string model = "Unknown", string year = "Unknown", string vin = "") =>
        new() { Manufacturer = make, Model = model, Year = year, VIN = vin };

    // 1. fresh launch
    [Fact]
    public void AFreshLaunch_HasNoVehicle_AndTheCardClaimsNone()
    {
        var session = new VehicleSessionIdentity();
        Assert.Null(session.Current);

        foreach (var english in new[] { true, false })
        {
            var lines = VehicleIdentityText.Disconnected(english);
            foreach (var text in new[] { lines.Name, lines.Vin, lines.Trim, lines.Fuel })
                Assert.DoesNotMatch("Chevrolet|Ford|Toyota|BMW", text);
        }
    }

    // 2. Chevrolet identified
    [Fact]
    public void AChevroletIdentifiedInTheSession_IsShownAsTheVehicle()
    {
        var session = new VehicleSessionIdentity();
        Assert.True(session.TrySet(session.Generation, Car("Chevrolet", "Volt", "2013", "1G1RA6E4XDU000001")));
        Assert.Equal("Chevrolet Volt 2013", VehicleIdentityText.Name(session.Current, true));
    }

    // 3. restart without a connection
    [Fact]
    public void ARestart_StartsEmpty_BecauseTheSessionIsMemoryOnly()
    {
        var first = new VehicleSessionIdentity();
        first.TrySet(first.Generation, Car("Chevrolet", "Volt", "2013"));

        var afterRestart = new VehicleSessionIdentity();                         // a new process builds a new session object
        Assert.Null(afterRestart.Current);
        Assert.Equal("Vehicle unknown", VehicleIdentityText.Name(afterRestart.Current, true));
        Assert.Equal("მანქანა უცნობია", VehicleIdentityText.Name(afterRestart.Current, false));
    }

    [Fact]
    public void TheSavedBrandChoice_IsNotWrittenIntoTheVehicleName()
    {
        var home = Read("ViewModels", "HomeViewModel.cs");
        var brandMethod = Regex.Match(home, @"private void ApplyBrandProfile\(VehicleBrandProfile profile\)\s*\{[\s\S]*?\n    \}\n").Value;
        Assert.NotEmpty(brandMethod);
        Assert.DoesNotContain("VehicleName", brandMethod);                       // the root cause: it used to assign profile.DisplayName here

        var applyInfo = Regex.Match(home, @"private void ApplyVehicleInfo\(VehicleInfo vehicle\)\s*\{[\s\S]*?\n    \}\n").Value;
        var code = string.Join("\n", applyInfo.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.DoesNotContain("resolved.DisplayName", code);

        // the constructor shows the disconnected card whenever the process starts without a connection
        Assert.Matches(@"ApplyBrandProfile\(_brand\.Current\);[\s\S]{0,260}if \(!_bluetooth\.IsConnected\)\s*ShowDisconnectedIdentity\(\);", home);
    }

    // 4. Ford identified
    [Fact]
    public void AFordIdentifiedInTheSession_IsShownAsTheVehicle()
    {
        var session = new VehicleSessionIdentity();
        session.TrySet(session.Generation, Car("Ford", "Focus", "2015"));
        Assert.Equal("Ford Focus 2015", VehicleIdentityText.Name(session.Current, true));
    }

    // 5. disconnection after identification
    [Fact]
    public void ADisconnect_ForgetsTheVehicle_AndTheCardGoesBackToUnknown()
    {
        var session = new VehicleSessionIdentity();
        session.TrySet(session.Generation, Car("Chevrolet", "Volt", "2013"));

        session.Invalidate();

        Assert.Null(session.Current);
        Assert.Equal("Vehicle unknown", VehicleIdentityText.Name(session.Current, true));
    }

    // 6. reconnection to a different vehicle
    [Fact]
    public void AReconnectionToAnotherCar_ShowsOnlyTheNewCar()
    {
        var session = new VehicleSessionIdentity();
        session.TrySet(session.Generation, Car("Chevrolet", "Volt", "2013"));
        session.Invalidate();                                                    // disconnect
        Assert.True(session.TrySet(session.Generation, Car("Ford", "Focus", "2015")));
        Assert.Equal("Ford Focus 2015", VehicleIdentityText.Name(session.Current, true));
    }

    // 7. failed VIN retrieval
    [Fact]
    public void AFailedVinRead_LeavesEveryFieldUnknown_NotInvented()
    {
        var info = new VehicleInfo { Protocol = "ISO 15765-4 CAN" };             // connected, but nothing identified
        Assert.Equal("Vehicle unknown", VehicleIdentityText.Name(info, true));
        Assert.False(VehicleIdentityText.HasValue(info.VIN));
        Assert.False(VehicleIdentityText.HasValue(info.Trim));
        Assert.False(VehicleIdentityText.HasValue(info.Model));
    }

    // 8. partially available identification
    [Fact]
    public void APartialIdentification_ShowsOnlyWhatWasObtained()
    {
        Assert.Equal("Ford", VehicleIdentityText.Name(Car("Ford"), true));
        Assert.Equal("Ford 2015", VehicleIdentityText.Name(Car("Ford", year: "2015"), true));
        Assert.Equal("Chevrolet Volt", VehicleIdentityText.Name(Car("Chevrolet", "Volt"), true));
    }

    // 9. delayed responses from a previous connection
    [Fact]
    public void ALateAnswerFromTheEarlierConnection_IsRefused_AndDoesNotOverwriteTheNewOne()
    {
        var session = new VehicleSessionIdentity();
        var oldConnection = session.Generation;
        session.Invalidate();                                                    // adapter disconnected while a read was in flight
        var newConnection = session.Generation;
        Assert.True(session.TrySet(newConnection, Car("Ford", "Focus", "2015")));

        Assert.False(session.TrySet(oldConnection, Car("Chevrolet", "Volt", "2013")));   // the old read finally answers
        Assert.Equal("Ford", session.Current!.Manufacturer);
    }

    [Fact]
    public void ALateAnswerAfterADisconnect_CannotBringTheOldCarBack()
    {
        var session = new VehicleSessionIdentity();
        var generation = session.Generation;
        session.Invalidate();
        Assert.False(session.TrySet(generation, Car("Chevrolet")));
        Assert.Null(session.Current);
    }

    [Fact]
    public void EveryPathThatPublishesAVehicle_IsGuardedByTheSessionGeneration()
    {
        var service = Read("Services", "VehicleInfoService.cs");
        Assert.DoesNotMatch(@"\bCurrentVehicle\s*=\s*[^>]", service);              // nothing assigns the current vehicle directly any more
        Assert.Equal(2, Regex.Matches(service, @"if \(!_session\.TrySet\(generation, info\)\)").Count);   // both one-shot reads
        Assert.Contains("if (generation != _session.Generation)", service);       // the progressive read
        var home = Read("ViewModels", "HomeViewModel.cs");
        Assert.Matches(@"OnVehicleInfoUpdated[\s\S]{0,400}if \(!_bluetooth\.IsConnected\)\s*return;", home);
    }

    // 10. history is kept
    [Fact]
    public void Disconnecting_ClearsOnlyTheSessionIdentity_NeverTheSavedHistory()
    {
        var service = Read("Services", "VehicleInfoService.cs");
        var clear = Regex.Match(service, @"public void ClearConnectedVehicleState\(\)\s*\{[\s\S]*?\n    \}\n").Value;
        Assert.NotEmpty(clear);
        Assert.Contains("_session.Invalidate()", clear);
        foreach (var forbidden in new[] { "_db", "Preferences", "Delete", "DropTable", "Remove(" })
            Assert.DoesNotContain(forbidden, clear);                              // identity cache, database and reports are untouched

        // the saved identity cache is still written for a verified VIN
        Assert.Contains("SaveIdentityCache(info)", service);
        Assert.Contains("SaveDatabaseCacheAsync(info)", service);
    }

    // 11-14. the menu
    private static string[] TileCommands => new[]
    {
        "GoToModulesCommand", "GoToMileageCommand", "GoToFuelCommand", "GoToTroubleCodesCommand", "GoToVehicleHealthCommand",
        "GoToLiveDataCommand", "GoToSensorsCommand", "GoToEmissionsCommand", "GoToFreezeFrameCommand", "GoToEcuCommand",
        "GoToHybridHealthCommand", "GoToBatteryCommand", "GoToGraphStudioCommand", "GoToCodingCommand", "GoToAccelerationCommand",
        "GoToServiceCommand", "GoToDiagnosticReportCommand", "GoToTechnicalInspectionCommand", "GoToAdapterCenterCommand",
        "GoToCapabilitiesCommand", "GoToSettingsCommand", "GoToResearchCommand"
    };

    [Fact]
    public void BeforeConnecting_EveryMenuTileIsStillThere_AndNoneIsHiddenOrSwitchedOffByTheConnectionState()
    {
        var xaml = Read("Views", "HomePage.xaml");
        foreach (var command in TileCommands)
            Assert.Contains("Command=\"{Binding " + command + "}\"", xaml);
        Assert.Contains("OnPrePurchaseTapped", xaml);

        // muted (opacity) while disconnected, but no tile is collapsed or disabled by IsConnected. The one exception
        // is the admin-only Research tile: it is hidden for a different, unrelated reason (not a PRO/non-PRO or
        // disconnected account), so it alone is allowed an IsVisible bound to IsAdmin, never to IsConnected.
        var tileBorders = Regex.Matches(xaml, @"<Border [^>]*ListCardStyle[^>]*Padding=""(12,11|6,12,6,10)""[^>]*>").Select(match => match.Value).ToList();
        Assert.Equal(23, tileBorders.Count);
        var adminTiles = tileBorders.Where(border => border.Contains("IsVisible=\"{Binding IsAdmin}\"")).ToList();
        Assert.Single(adminTiles);
        Assert.All(tileBorders.Except(adminTiles), border =>
        {
            Assert.DoesNotContain("IsVisible=", border);
            Assert.DoesNotContain("IsEnabled=", border);
        });
        Assert.DoesNotMatch(@"<controls:FlowGrid [^>]*IsEnabled=", xaml);
    }

    [Fact]
    public void AfterConnecting_TheSameTilesBecomeOpaque_AndNothingNewIsEnabledAutomatically()
    {
        var xaml = Read("Views", "HomePage.xaml");
        // 16 car tiles are muted until connected and become opaque on IsConnected - that is all the connection changes on Home
        Assert.Equal(16, Regex.Matches(xaml, @"ListCardStyle[^>]*Opacity=""0\.4""").Count);
        Assert.Equal(17, Regex.Matches(xaml, @"<DataTrigger TargetType=""(Border|controls:FlowGrid)"" Binding=""\{Binding IsConnected\}"" Value=""True"">").Count);   // 16 tiles + the PRO group
        // each page still decides for itself what it supports; Home has no list of "enabled OEM functions"
        Assert.DoesNotContain("EnableOem", Read("ViewModels", "HomeViewModel.cs"));
    }

    [Fact]
    public void ATapOnACarFeatureWithoutAnAdapter_ExplainsWhatToDo()
    {
        var home = Read("ViewModels", "HomeViewModel.cs");
        var require = Regex.Match(home, @"private async Task<bool> RequireObdConnectionAsync\(\)\s*\{[\s\S]*?\n    \}\n").Value;
        Assert.Contains("DisplayAlertLocalizedAsync", require);
        Assert.Contains("OBD adapter needed", require);
        Assert.Contains("OBD ადაპტერი საჭიროა", require);                       // Georgian and English; other languages through the packs
        Assert.Contains("if (_connectPromptOpen)", require);                      // one prompt at a time
        foreach (var command in new[] { "GoToTroubleCodes", "GoToLiveData", "GoToSensors", "GoToEmissions", "GoToFreezeFrame", "GoToEcu", "GoToHybridHealth", "GoToBattery", "GoToService", "GoToMileage", "GoToFuel", "GoToModules", "GoToDiagnosticReport" })
            Assert.Matches("private async Task " + command + @"\(\)[\s\S]{0,500}RequireObdConnectionAsync\(\)", home);
    }

    [Fact]
    public void SettingsAndAdapter_OpenWithoutAConnection()
    {
        var home = Read("ViewModels", "HomeViewModel.cs");
        foreach (var command in new[] { "GoToAdapterCenter", "GoToSettings", "GoToCoding", "GoToGraphStudio" })
        {
            var start = home.IndexOf("private async Task " + command + "()", StringComparison.Ordinal);
            Assert.True(start >= 0, command);
            var next = home.IndexOf("[RelayCommand]", start, StringComparison.Ordinal);
            var body = home.Substring(start, (next < 0 ? home.Length : next) - start);
            Assert.DoesNotContain("RequireObdConnectionAsync", body);
        }
    }

    [Fact]
    public void UnavailableTiles_StillFlowSequentially_WithNoEmptyCell()
    {
        // all 22 tiles are always visible, so they always take consecutive cells
        var cells = FlowPlacement.Cells(Enumerable.Repeat(true, 6).ToArray(), 3);
        Assert.Equal(new[] { (0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2) }, cells);
        // and a tile that is ever hidden still leaves no hole
        var withHidden = FlowPlacement.Cells(new[] { true, false, true, true }, 3);
        Assert.Equal(new[] { (0, 0), (0, 1), (0, 2) }, new[] { withHidden[0], withHidden[2], withHidden[3] });
    }
}
