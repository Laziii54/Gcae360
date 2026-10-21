using System.Text.RegularExpressions;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// The visible product name is OBD360 and the supplied logo pack is what ships. Technical identifiers that must not change
/// (package / bundle id, support mailbox, DataProtection purposes, bank payment descriptors, internal hash salts) are listed
/// explicitly. Source-level checks only - they say nothing about how a launcher icon or splash looks on a device.
/// </summary>
public sealed class BrandingTests
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

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root }.Concat(parts).ToArray()));

    private static (int Width, int Height) PngSize(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)));
    }

    [Fact]
    public void TheProjectKeepsTheStoreIdentity_AndShowsOBD360()
    {
        var csproj = Read("CarScanner.csproj");
        Assert.Contains("<ApplicationTitle>OBD360</ApplicationTitle>", csproj);
        Assert.Contains("<ApplicationId>com.obd360.app</ApplicationId>", csproj);        // Android: the Play package, never changes after the Play app exists
        Assert.Contains("== 'ios'", csproj);
        Assert.Contains(">com.gcar360.app</ApplicationId>", csproj);                        // iOS bundle id: the App Store Connect record, never changes
        Assert.Contains("obd360_splash.png", csproj);
        Assert.DoesNotContain("gcar_splash", csproj);
    }

    [Theory]
    [InlineData("Resources/AppIcon/appicon.png", 1024, 1024)]
    [InlineData("Resources/Splash/obd360_splash.png", 1024, 1024)]
    [InlineData("Resources/Images/obd360_loading.png", 512, 512)]
    [InlineData("Resources/Images/obd360_mark.png", 256, 256)]   // square app-icon tile, no transparent margin
    [InlineData("Platforms/Android/Resources/mipmap-xxxhdpi/ic_gcar_light.png", 192, 192)]
    [InlineData("Platforms/Android/Resources/mipmap-xxxhdpi/ic_gcar_dark_round.png", 192, 192)]
    [InlineData("Platforms/Android/Resources/mipmap-xxxhdpi/ic_gcar_light_foreground.png", 432, 432)]
    [InlineData("Platforms/Android/Resources/mipmap-mdpi/ic_gcar_dark_foreground.png", 108, 108)]
    [InlineData("Backend/CarScanner.Api/wwwroot/assets/logo.png", 256, 256)]
    [InlineData("Backend/CarScanner.Api/wwwroot/assets/favicon.png", 128, 128)]
    [InlineData("Backend/CarScanner.Api/wwwroot/assets/apple-touch-icon.png", 180, 180)]
    [InlineData("Backend/CarScanner.Api/wwwroot/assets/icon-512.png", 512, 512)]
    public void TheLogoAssets_ExistAtTheirPlatformSizes(string relative, int width, int height) =>
        Assert.Equal((width, height), PngSize(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar))));

    [Fact]
    public void TheOldAnimatedLogoAndSplash_AreNoLongerShipped()
    {
        foreach (var gone in new[] { "Resources/Splash/gcar_splash.png", "Resources/Images/gcar_loading.gif", "Backend/CarScanner.Api/wwwroot/assets/loader.gif", "Backend/CarScanner.Api/wwwroot/assets/loader.webp" })
            Assert.False(File.Exists(Path.Combine(Root, gone.Replace('/', Path.DirectorySeparatorChar))), gone);

        Assert.DoesNotContain("gcar_loading", Read("Views", "LoadingPage.xaml"));
        Assert.Contains("obd360_loading.png", Read("Views", "LoadingPage.xaml"));
    }

    [Fact]
    public void TheHeaders_ShowTheLogoInsteadOfALetterPlaceholder()
    {
        Assert.Contains("obd360_mark.png", Read("Views", "HomePage.xaml"));
        Assert.Contains("obd360_mark.png", Read("Views", "StartPage.xaml"));
        Assert.DoesNotContain("Text=\"G\"", Read("Views", "HomePage.xaml"));
        Assert.DoesNotContain("Text=\"360°\"", Read("Views", "StartPage.xaml"));
    }

    [Fact]
    public void TheAdaptiveIconBackground_IsThePureBlackOfTheLogoArt()
    {
        var colors = Read("Platforms", "Android", "Resources", "values", "colors.xml");
        Assert.Contains("name=\"ic_gcar_light_background\">#000000<", colors);
        Assert.Contains("name=\"ic_gcar_dark_background\">#000000<", colors);
    }

    [Fact]
    public void BackendPagesAndEmails_NameTheProductOBD360_AndOnlyKeepTheListedTechnicalIdentifiers()
    {
        // GCar360 may remain only as: the support mailbox, DataProtection purposes (changing them invalidates tokens), bank payment
        // descriptors, package / bundle ids, and internal hash salts. Everything else a person can read must say OBD360.
        var allowed = new[]
        {
            "com.gcar360.app", "GCar360.AccountDelete.v1", "GCar360.Admin.v2",
            "\"GCar360 PRO\"", "\"GCar360 VIN \"", "gcar360-ranking:", "gcar360-video:", "gcar360-qr:", "gcar360-device:"
        };
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "Backend", "CarScanner.Api"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Root, file);
            if (relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = allowed.Aggregate(lines[i], (current, token) => current.Replace(token, string.Empty, StringComparison.Ordinal));
                if (Regex.IsMatch(line, "GCar|G-Car|Georgian Car Scanner") && !Regex.IsMatch(line.TrimStart(), @"^(//|\*)"))
                    offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    [Fact]
    public void TheWebsiteUsesTheNewFaviconAndLogoFiles()
    {
        var site = Read("Backend", "CarScanner.Api", "Services", "SitePages.cs");
        Assert.Contains("/assets/favicon.png", site);
        Assert.Contains("/assets/icon-512.png", site);
        Assert.DoesNotContain("loader.gif", site);
        Assert.DoesNotContain("loader.webp", site);
    }

    [Fact]
    public void ThePublicPages_LiveOnTheOwnDomain_WhileTheApiAddressIsConfiguredSeparately()
    {
        var links = Read("Services", "StoreLinks.cs");
        Assert.Contains("\"https://obd360.ge\"", links);
        Assert.DoesNotContain("azurewebsites", links);

        // The API base (accounts, AI, payments) is not switched with the website: it moves only when the domain serves the API.
        Assert.Contains("BackendBaseUrl", Read("Services", "AIConfig.cs"));

        Assert.Contains("https://obd360.ge", Read("Backend", "CarScanner.Api", "appsettings.example.json"));
    }
}
