using CarScanner.Services;
using Xunit;

namespace CarScanner.Tests;

/// <summary>Pull-to-refresh rules: one real refresh at a time, a tap of feedback only after a completed refresh.</summary>
public sealed class PullRefreshTests
{
    [Fact]
    public async Task APull_RunsTheRefreshOnce_ShowsTheSpinner_AndTapsWhenDone()
    {
        var spinner = new List<bool>();
        var runs = 0;
        var taps = 0;

        var ran = await PullRefreshRunner.RunAsync(
            () => { runs++; return Task.CompletedTask; },
            () => false,
            spinner.Add,
            () => taps++);

        Assert.True(ran);
        Assert.Equal(1, runs);
        Assert.Equal(1, taps);
        Assert.Equal(new[] { true, false }, spinner);
    }

    [Fact]
    public async Task APullWhileAnotherRefreshRuns_IsDropped_WithoutASecondRequestOrATap()
    {
        var spinner = new List<bool>();
        var runs = 0;
        var taps = 0;

        var ran = await PullRefreshRunner.RunAsync(() => { runs++; return Task.CompletedTask; }, () => true, spinner.Add, () => taps++);

        Assert.False(ran);
        Assert.Equal(0, runs);
        Assert.Equal(0, taps);
        Assert.Equal(new[] { false }, spinner);                   // the spinner the control started is switched off again
    }

    [Fact]
    public async Task ARefreshThatIsCancelled_StopsTheSpinner_AndDoesNotTap()
    {
        var spinner = new List<bool>();
        var taps = 0;

        var ran = await PullRefreshRunner.RunAsync(() => Task.FromCanceled(new CancellationToken(true)), () => false, spinner.Add, () => taps++);

        Assert.False(ran);
        Assert.Equal(0, taps);
        Assert.Equal(new[] { true, false }, spinner);
    }

    [Fact]
    public async Task ARefreshThatFails_StopsTheSpinner_AndTheErrorStillReachesTheCaller()
    {
        var spinner = new List<bool>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PullRefreshRunner.RunAsync(() => throw new InvalidOperationException("adapter"), () => false, spinner.Add));

        Assert.Equal(new[] { true, false }, spinner);
    }

    [Fact]
    public async Task AFailingTap_NeverBreaksTheRefresh()
    {
        var ran = await PullRefreshRunner.RunAsync(() => Task.CompletedTask, () => false, _ => { }, () => throw new InvalidOperationException("no vibration"));

        Assert.True(ran);
    }

    [Fact]
    public void ServiceAndBatteryPages_AreWrappedInARefreshView_BoundToTheirOwnBusyGuard()
    {
        var root = AppContext.BaseDirectory.Split("Tests")[0];
        foreach (var page in new[] { "ServicePage.xaml", "BatteryPage.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(root, "Views", page));
            Assert.Contains("<RefreshView", xaml);
            Assert.Contains("IsRefreshing=\"{Binding IsPullRefreshing}\"", xaml);
            Assert.Contains("Command=\"{Binding PullRefreshCommand}\"", xaml);
        }

        // The command asks the page's own flags, never the RefreshView flag (which is already true when it runs).
        var service = File.ReadAllText(Path.Combine(root, "ViewModels", "ServiceViewModel.Simple.cs"));
        Assert.Contains("PullRefreshRunner.RunAsync", service);
        Assert.Contains("IsBusy || IsOilResetRunning || IsDiscoveryRunning", service);
        var battery = File.ReadAllText(Path.Combine(root, "ViewModels", "BatteryViewModel.cs"));
        Assert.Contains("PullRefreshRunner.RunAsync", battery);
    }
}
