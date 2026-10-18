namespace CarScanner.Tests;

/// <summary>
/// Which checks are free and which are PRO: the mileage check is free for everyone, the hybrid / HV battery check
/// (and its load test) is PRO. Source-reading tests, like the other page-wiring tests: they pin the decision so a
/// refactor cannot silently flip it. Unit/source only, not a device check.
/// </summary>
public sealed class ProTierTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var next = source.IndexOf("\n    [RelayCommand]", start + signature.Length, StringComparison.Ordinal);
        return source.Substring(start, (next < 0 ? Math.Min(source.Length - start, 3000) : next - start));
    }

    [Fact]
    public void TheMileageCheck_IsFree_NoProGateAndNoProMark()
    {
        var vm = Read("ViewModels", "MileageViewModel.cs");
        var check = vm.Substring(vm.IndexOf("private async Task CheckAsync()", StringComparison.Ordinal), 700);
        Assert.DoesNotContain("ProGate", check);
        Assert.DoesNotContain("ProGate.Suffix", vm);

        var home = Read("Views", "HomePage.xaml");
        var tap = home.IndexOf("Binding GoToMileageCommand", StringComparison.Ordinal);
        var tileEnd = home.IndexOf("</Border>", tap, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowProBadges", home.Substring(tap, tileEnd - tap + 1));
    }

    [Fact]
    public void TheHybridBatteryCheck_IsPro_BothTheCheckAndTheLoadTest()
    {
        var vm = Read("ViewModels", "HybridHealthViewModel.cs");
        Assert.Contains("ProGate.EnsureOrOpenAsync()", Method(vm, "private async Task Scan()"));
        Assert.Contains("ProGate.EnsureOrOpenAsync()", Method(vm, "private async Task StartLoadTest()"));
        Assert.Contains("L(\"შემოწმება\", \"Check\") + ProGate.Suffix", vm);
        Assert.Contains("L(\"ტესტის დაწყება\", \"Start test\") + ProGate.Suffix", vm);

        // the page itself stays open for everyone; the PRO mark is refreshed when it appears again (after the PRO page)
        Assert.Contains("RefreshProState()", Read("Views", "HybridHealthPage.xaml.cs"));
    }

    [Fact]
    public void TheWebsite_ListsTheMileageCheckAsFree_AndTheHybridBatteryCheckAsPro()
    {
        var site = Read("Backend", "CarScanner.Api", "Services", "SitePages.cs");
        var free = site.IndexOf("var free = string.Concat(", StringComparison.Ordinal);
        var pro = site.IndexOf("var pro = string.Concat(", StringComparison.Ordinal);
        Assert.True(free >= 0 && pro > free);
        Assert.Contains("\"Mileage check\"", site.Substring(free, pro - free));
        Assert.DoesNotContain("\"Mileage check\"", site.Substring(pro, 2400));
        Assert.Contains("\"Hybrid battery check\"", site.Substring(pro, 2400));
    }
}
