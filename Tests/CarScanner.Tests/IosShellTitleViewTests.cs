using System.Text.RegularExpressions;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// Guards for the iOS crash `ObjCRuntime.Runtime.MissingCtor` seen around Shell TitleView creation. The root cause is not
/// established (no crash log is in the repository); these checks only keep the code-level candidates closed. They read
/// source files — nothing here runs on an iPhone.
/// </summary>
public sealed class IosShellTitleViewTests
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
    public void EveryManagedUIViewInTheIosFolder_DeclaresTheNativeHandleConstructor()
    {
        // A managed UIView subclass without the (NativeHandle) constructor cannot be re-wrapped when UIKit hands the native
        // view back after its managed object was collected — the exact MissingCtor failure.
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "Platforms", "iOS"), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(text, @"class\s+(\w+)\s*:\s*UIView\b"))
                if (!Regex.IsMatch(text, $@"public\s+{match.Groups[1].Value}\s*\(\s*NativeHandle\s+\w+\s*\)"))
                    offenders.Add($"{Path.GetFileName(file)}: {match.Groups[1].Value}");
        }

        Assert.True(offenders.Count == 0, "UIView subclasses without a NativeHandle constructor: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TitleViews_AreOnlyTheKnownTwo_AndHostNoCustomNativeView()
    {
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Root, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                    !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(text, @"<Shell\.TitleView>[\s\S]*?</Shell\.TitleView>"))
            {
                found.Add(Path.GetFileName(file));
                // The title bar must not contain a view with a custom platform handler (camera preview, native controls).
                Assert.DoesNotContain("PerformanceCameraView", match.Value);
                Assert.DoesNotContain("<WebView", match.Value);
            }
        }

        // A new TitleView must be added here on purpose: each one is a place the MAUI iOS renderer creates a native container.
        Assert.Equal(new[] { "AccelerationPage.xaml", "AppShell.xaml" }, found.OrderBy(name => name).ToArray());
    }

    [Fact]
    public void TheStatusViewModelBehindTheTitleView_ChangesItsBoundTextOnlyOnTheMainThread()
    {
        var code = File.ReadAllText(Path.Combine(Root, "ViewModels", "ShellStatusViewModel.cs"));
        // Every event handler that touches bound properties hops to the main thread first.
        foreach (var handler in new[] { "OnConnectionChanged", "OnProChanged", "OnVehicleChanged" })
            Assert.Matches($@"{handler}\([^)]*\)\s*=>\s*MainThread\.BeginInvokeOnMainThread", code);
    }

    [Fact]
    public void ThePageTitleView_IsRebuiltWithThePage_NotSharedBetweenPages()
    {
        // The page-level title view lives in the page's own XAML (created with the page); only the Shell-level one is shared.
        var xaml = File.ReadAllText(Path.Combine(Root, "Views", "AccelerationPage.xaml"));
        Assert.Contains("<Shell.TitleView>", xaml);
        var code = File.ReadAllText(Path.Combine(Root, "Views", "AccelerationPage.xaml.cs"));
        Assert.Contains("PageTitleView.BindingContext = Shell.Current?.BindingContext;", code);
    }
}
