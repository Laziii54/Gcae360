namespace CarScanner.Tests;

public sealed class SensorGraphTitleTests
{
    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "ViewModels", "SensorGraphViewModel.cs"));
    }

    [Fact]
    public void TheGraphTitle_DropsTheTechnicalPidCode()
    {
        var source = Source();
        Assert.Contains("LivePidItem pid => (PlainName(pid.DisplayName), pid.Unit)", source);
        var pattern = System.Text.RegularExpressions.Regex.Match(source, @"Regex\.Replace\(name, @""([^""]+)""").Groups[1].Value;
        Assert.Equal("მანძილი ანთებული MIL-ით",
            System.Text.RegularExpressions.Regex.Replace("მანძილი ანთებული MIL-ით · PID 0121", pattern, string.Empty));
        Assert.Equal("Engine RPM", System.Text.RegularExpressions.Regex.Replace("Engine RPM", pattern, string.Empty));
    }
}
