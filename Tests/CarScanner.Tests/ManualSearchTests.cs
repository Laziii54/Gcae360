using CarScanner.Models;
using CarScanner.Services.Mileage;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Tests;

/// <summary>
/// "Enter manually" on the Service and Mileage pages: the person types what the dashboard shows and the app marks the readings
/// that are CLOSE to it (a dashboard and a module rarely show the same number). Pure logic plus source-reading checks of the pages;
/// nothing here was tried on a phone or a car.
/// </summary>
public sealed class ManualSearchTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray())).Replace("\r\n", "\n");
    }

    private static MileageEvidence Odo(string ecu, double km, MileageMeasurement measurement = MileageMeasurement.TotalOdometer, string method = "22 F40D") =>
        new("test", ecu, km, 0.9, "t") { Measurement = measurement, Method = method };

    [Theory]
    [InlineData("244418", 244418)]
    [InlineData("244 418", 244418)]
    [InlineData("244,418", 244418)]
    [InlineData("244.418", 244418)]
    [InlineData("1,244,418", 1244418)]
    [InlineData("244418.5", 244418.5)]
    [InlineData("84%", 84)]
    [InlineData(" 84 ", 84)]
    public void WhatThePersonTypes_IsRead(string typed, double expected)
    {
        Assert.True(MileageManualMatch.TryParseTyped(typed, out var value));
        Assert.Equal(expected, value, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-")]
    [InlineData("99999999")]
    public void NothingUsable_IsRefused(string? typed) =>
        Assert.False(MileageManualMatch.TryParseTyped(typed, out _));

    [Fact]
    public void ACloseReading_IsMarkedClose_AndTheClosestIsFirst()
    {
        var readings = new[] { Odo("Instrument cluster", 244_418), Odo("Engine ECU", 244_430), Odo("ECU 7B0", 251_000) };

        var ranked = MileageManualMatch.Rank(readings, 244_420, typedInMiles: false);

        Assert.Equal("Instrument cluster", ranked[0].Evidence.Ecu);
        Assert.True(ranked[0].IsClose);
        Assert.True(ranked[1].IsClose);              // 10 km away: inside the 0.5 % window (1 222 km)
        Assert.False(ranked[2].IsClose);             // 6 580 km away
    }

    [Fact]
    public void TheWindow_IsHalfAPercent_ButNeverNarrowerThanFiveKilometres()
    {
        Assert.Equal(5, MileageManualMatch.ToleranceKm(100), 6);
        Assert.Equal(500, MileageManualMatch.ToleranceKm(100_000), 6);
    }

    [Fact]
    public void MilesTypedFromTheDashboard_AreComparedWithKilometresReadFromTheCar()
    {
        var ranked = MileageManualMatch.Rank(new[] { Odo("Instrument cluster", 160_934) }, 100_000, typedInMiles: true);   // 100 000 mi = 160 934 km

        Assert.True(ranked[0].IsClose);
        Assert.Equal(0, ranked[0].DifferenceKm, 0);
    }

    [Fact]
    public void OnlyTotalOdometerReadings_TakePart_AndTheSameReadingIsListedOnce()
    {
        var readings = new[]
        {
            Odo("Engine ECU", 120_000),
            Odo("Engine ECU", 120_000),                                                                  // the same reading twice
            Odo("OBD-II", 119_990, MileageMeasurement.DistanceSinceDtcClear, "01 31"),                  // not an odometer
            Odo("OBD-II", 119_995, MileageMeasurement.TripDistance, "01 A6")
        };

        var ranked = MileageManualMatch.Rank(readings, 120_000, false);

        Assert.Single(ranked);
    }

    [Theory]
    [InlineData(84, 84, true)]
    [InlineData(84, 86, true)]
    [InlineData(84, 82, true)]
    [InlineData(84, 87, false)]
    [InlineData(84, 81, false)]
    public void OilLife_CountsAsTheSameWithinTwoPoints(double typed, double read, bool expected)
    {
        var hit = new ServiceSearchHit("k", "GM", "Oil life", ServiceMetricKind.OilLifePercent, read, "%", "221234", "ECM", true);

        Assert.Equal(expected, ServiceSearchMatching.MatchesTyped(hit, typed, null));
    }

    [Fact]
    public void ADistanceToTheNextService_IsMatchedByDistance_InKilometresOrMiles_AndNeverByOilLife()
    {
        var km = new ServiceSearchHit("a", "VW", "To service", ServiceMetricKind.DistanceToNextService, 4_150, "km", "22F1", "ECU", true);
        var miles = new ServiceSearchHit("b", "Ford", "To service", ServiceMetricKind.DistanceToNextService, 2_600, "mi", "22F2", "PCM", true);
        var oil = new ServiceSearchHit("c", "GM", "Oil life", ServiceMetricKind.OilLifePercent, 84, "%", "22F3", "ECM", true);

        Assert.True(ServiceSearchMatching.MatchesTyped(km, null, 4_200));        // within 100 km
        Assert.False(ServiceSearchMatching.MatchesTyped(km, null, 4_500));
        Assert.True(ServiceSearchMatching.MatchesTyped(miles, null, 4_190));     // 2 600 mi = 4 184 km
        Assert.False(ServiceSearchMatching.MatchesTyped(oil, null, 84));         // a % is never a distance
        Assert.False(ServiceSearchMatching.MatchesTyped(km, 84, null));          // a distance is never a %
    }

    [Fact]
    public void TheMatchingKeepsItsOldShape_SoTheExistingSearchStillWorks()
    {
        var hits = new[] { new ServiceSearchHit("k", "GM", "Oil life", ServiceMetricKind.OilLifePercent, 85, "%", "221234", "ECM", true) };

        Assert.True(ServiceSearchMatching.MatchDashboard(hits, 84)[0].MatchesDashboard);
        Assert.False(ServiceSearchMatching.MatchDashboard(hits, null)[0].MatchesDashboard);
    }

    [Fact]
    public void BothPages_HaveAnEnterManuallyRow_ThatIsCollapsedUntilTapped()
    {
        var mileage = Read("Views", "MileagePage.xaml");
        Assert.Contains("Text=\"{Binding ManualCaption}\"", mileage);
        Assert.Contains("Command=\"{Binding ToggleManualCommand}\"", mileage);
        Assert.Contains("IsVisible=\"{Binding ManualOpen}\"", mileage);
        Assert.Contains("Command=\"{Binding SearchManualCommand}\"", mileage);
        Assert.Contains("MileageManualMatch.Rank(", Read("ViewModels", "MileageViewModel.cs"));

        var service = Read("Views", "ServicePage.xaml");
        Assert.Contains("Text=\"{Binding ManualSearchCaption}\"", service);
        Assert.Contains("Command=\"{Binding ToggleSearchCommand}\"", service);
        Assert.Contains("IsVisible=\"{Binding ShowSearch}\"", service);
        Assert.Contains("Command=\"{Binding RunQuickOilDiscoveryCommand}\"", service);
        Assert.Contains("TapGestureRecognizer Tapped=\"OnSearchHitTapped\"", service);   // a close value is remembered for this car on a tap
    }

    [Fact]
    public void TheNewTexts_NeverClaimAnOriginalOrVerifiedMileage_OrAccuseAnyone()
    {
        var banned = new[] { "original", "genuine", "verified", "authentic", "rolled back", "rollback", "tampered", "fraud" };
        var vm = Read("ViewModels", "MileageViewModel.cs");

        // The texts of the manual search: its properties and the search with its result lines.
        var properties = Between(vm, "// \"Enter manually\": the person types", "    // Log");
        var search = Between(vm, "// Enter manually: which of the readings is close", "/// <summary>The readings become the");
        var service = Between(Read("ViewModels", "ServiceViewModel.Simple.cs"), "public string ManualSearchCaption", "public string HelpRecorderTitle");

        foreach (var block in new[] { properties, search, service })
            foreach (var word in banned)
                Assert.DoesNotContain(word, block.ToLowerInvariant());
    }

    private static string Between(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, from);
        var end = text.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, to);
        return text[start..end];
    }
}
