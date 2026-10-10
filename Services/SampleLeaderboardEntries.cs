#if DEBUG
using CarScanner.Models;

namespace CarScanner.Services;

/// <summary>
/// 20 sample drivers for the 0–100 ranking, so the page can be judged with a full list while the real ranking is
/// still empty. They are shown exactly like real entries, but this file only exists in DEBUG builds
/// (Visual Studio / emulator): a RELEASE build never contains it, so the store version cannot show invented drivers.
/// </summary>
internal static class SampleLeaderboardEntries
{
    private static readonly (string User, string Car, double Seconds, bool Verified, int Days)[] Drivers =
    {
        ("Luka T.",    "Porsche Macan 2019",       5.4, true,   3),
        ("Nika B.",    "BMW M340i 2021",           6.1, true,   9),
        ("Giorgi M.",  "Tesla Model 3 2020",       7.0, true,  14),
        ("Sandro K.",  "Volkswagen Golf GTI 2016", 7.2, false,  5),
        ("Dato L.",    "Audi A4 2017",             7.6, false, 21),
        ("Irakli S.",  "Mercedes-Benz C300 2016",  7.9, true,  27),
        ("Levan G.",   "Subaru WRX 2015",          8.1, false, 11),
        ("Tornike A.", "Toyota Camry 2018",        8.4, false,  2),
        ("Mate R.",    "BMW 330i 2018",            8.6, false, 33),
        ("Beka J.",    "Hyundai Sonata 2017",      8.9, false, 18),
        ("Zura D.",    "Kia Optima 2016",          9.2, false, 40),
        ("Gela N.",    "Honda Civic 2016",         9.5, false,  7),
        ("Ana P.",     "Mazda 3 2015",             9.8, false, 25),
        ("Saba C.",    "Ford Focus 2014",         10.1, false, 12),
        ("Vakho Z.",   "Nissan Altima 2015",      10.4, false, 48),
        ("Tako V.",    "Lexus IS250 2011",        10.8, false,  1),
        ("Rati E.",    "Chevrolet Malibu 2017",   11.2, false, 30),
        ("Shota H.",   "Toyota Prius 2012",       11.9, false, 16),
        ("Mari O.",    "Opel Astra 2013",         12.6, false, 53),
        ("Kakha F.",   "Renault Logan 2014",      13.8, false, 22),
    };

    /// <summary>The same drivers for each test; times are scaled and ¼ mile gets a finish-line speed.</summary>
    public static IReadOnlyList<AccelerationLeaderboardEntry> For(string testId)
    {
        var (factor, trap) = testId switch
        {
            "0-60mph" => (0.96, 0),
            "quarter-mile" => (2.55, 1),
            _ => (1.0, 0)
        };

        var now = DateTime.UtcNow;
        return Drivers.Select((driver, index) =>
        {
            var seconds = Math.Round(driver.Seconds * factor, 2);
            return new AccelerationLeaderboardEntry
            {
                SubmissionId = $"s{index + 1:00}-{testId}",
                UserName = driver.User,
                VehicleModel = driver.Car,
                DurationSeconds = seconds,
                FinishedAt = now.AddDays(-driver.Days).AddMinutes(-index * 37),
                HasVerifiedVideo = driver.Verified,
                Status = driver.Verified ? "Verified" : "Approved",
                MaxSpeedKph = trap == 1 ? (int)Math.Round(214 - driver.Seconds * 4.2) : 0,
                IsDemo = false
            };
        }).ToList();
    }
}
#endif
