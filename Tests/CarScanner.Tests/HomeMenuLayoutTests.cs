using System.Text.RegularExpressions;
using CarScanner.Controls;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// The home menu tiles flow into the next free cell instead of sitting at hand-written Grid.Row / Grid.Column positions
/// (a removed or hidden tile used to leave a hole). Source and logic checks only - nothing here renders a screen.
/// </summary>
public sealed class HomeMenuLayoutTests
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

    [Fact]
    public void VisibleTiles_TakeTheNextFreeCell_LeftToRightThenDown()
    {
        var cells = FlowPlacement.Cells(new[] { true, true, true, true }, 3);
        Assert.Equal(new[] { (0, 0), (0, 1), (0, 2), (1, 0) }, cells);
    }

    [Fact]
    public void AHiddenTile_LeavesNoGap_AndTheNextTileMovesUp()
    {
        // Service | (hidden) | Vehicle report | Technical inspection  ->  three tiles on one row, no hole
        var visible = new[] { true, false, true, true };
        var cells = FlowPlacement.Cells(visible, 3);
        Assert.Equal((0, 0), cells[0]);
        Assert.Equal((0, 1), cells[2]);
        Assert.Equal((0, 2), cells[3]);
        Assert.Equal(1, FlowPlacement.Rows(visible, 3));
    }

    [Theory]
    [InlineData(0, 3, 1)]
    [InlineData(1, 3, 1)]
    [InlineData(3, 3, 1)]
    [InlineData(4, 3, 2)]
    [InlineData(6, 3, 2)]
    [InlineData(4, 2, 2)]
    public void TheRowCount_FollowsTheVisibleTiles(int tiles, int columns, int rows) =>
        Assert.Equal(rows, FlowPlacement.Rows(Enumerable.Repeat(true, tiles).ToArray(), columns));

    [Fact]
    public void TheHomeMenuTiles_HaveNoHandWrittenCells_AndKeepEveryCommand()
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "Views", "HomePage.xaml"));

        // tile cards must not carry their own Grid.Row / Grid.Column
        Assert.DoesNotMatch(@"<Border Grid\.(Row|Column)=""\d""[^>]*ListCardStyle", xaml);
        Assert.Equal(5, Regex.Matches(xaml, "<controls:FlowGrid ").Count);

        // every menu destination is still there (the header also repeats Settings)
        foreach (var command in new[]
                 {
                     "GoToModulesCommand", "GoToMileageCommand", "GoToFuelCommand", "GoToTroubleCodesCommand", "GoToVehicleHealthCommand",
                     "GoToLiveDataCommand", "GoToSensorsCommand", "GoToEmissionsCommand", "GoToFreezeFrameCommand", "GoToEcuCommand",
                     "GoToHybridHealthCommand", "GoToBatteryCommand", "GoToGraphStudioCommand", "GoToCodingCommand", "GoToAccelerationCommand",
                     "GoToServiceCommand", "GoToDiagnosticReportCommand", "GoToTechnicalInspectionCommand", "GoToAdapterCenterCommand",
                     "GoToCapabilitiesCommand", "GoToSettingsCommand"
                 })
            Assert.True(xaml.Split("Command=\"{Binding " + command + "}\"").Length - 1 >= 1, command);
        Assert.Contains("OnPrePurchaseTapped", xaml);
    }
}
