using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Background;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// A fuel / energy trip that keeps recording with the app in the background or the screen off.
/// What is tested here: the engine's watchdog against the software adapter (real time, short intervals), the keep-alive's
/// state logic, and the wiring in the source files. Nothing here ran on a phone: whether Android or iOS keep the app
/// alive for hours with the screen off can only be seen on a real device and a real drive.
/// </summary>
[Collection("TripBackground")]
public sealed class TripBackgroundTests
{
    private static async Task<(LiveDataEngine Engine, SimulatedElm327 Sim)> CreateAsync()
    {
        var sim = new SimulatedElm327();
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        var engine = new LiveDataEngine(obd, sim, NullLogger<LiveDataEngine>.Instance);
        return (engine, sim);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        return condition();
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray())).Replace("\r\n", "\n");
    }

    // ------------------------------------------------------------------ the engine's trip watchdog

    [Fact]
    public async Task ARecordingTrip_GetsItsPollingBack_WhenSomethingStopsItByForce()
    {
        var previous = LiveDataEngine.TripWatchdogInterval;
        LiveDataEngine.TripWatchdogInterval = TimeSpan.FromMilliseconds(150);
        var (engine, _) = await CreateAsync();
        try
        {
            engine.KeepRunningForTrip = true;
            await engine.StartAsync();
            Assert.True(await WaitUntilAsync(() => engine.IsRunning, TimeSpan.FromSeconds(3)));

            await engine.StopAsync(force: true);   // e.g. another page, or a resume that raced the old loop's shutdown
            Assert.False(engine.IsRunning);

            Assert.True(await WaitUntilAsync(() => engine.IsRunning, TimeSpan.FromSeconds(6)),
                "Polling stayed stopped although a trip is recording — the trip would freeze silently in the background.");
        }
        finally
        {
            engine.KeepRunningForTrip = false;
            await engine.StopAsync(force: true);
            LiveDataEngine.TripWatchdogInterval = previous;
        }
    }

    [Fact]
    public async Task WithoutATrip_PollingThatWasStopped_StaysStopped()
    {
        var previous = LiveDataEngine.TripWatchdogInterval;
        LiveDataEngine.TripWatchdogInterval = TimeSpan.FromMilliseconds(150);
        var (engine, _) = await CreateAsync();
        try
        {
            await engine.StartAsync();
            Assert.True(await WaitUntilAsync(() => engine.IsRunning, TimeSpan.FromSeconds(3)));

            await engine.StopAsync(force: true);
            await Task.Delay(TimeSpan.FromMilliseconds(900));

            Assert.False(engine.IsRunning, "The watchdog restarted polling that nobody wanted (no trip is recording).");
        }
        finally
        {
            await engine.StopAsync(force: true);
            LiveDataEngine.TripWatchdogInterval = previous;
        }
    }

    [Fact]
    public async Task WhenTheTripEnds_TheWatchdogGoesQuiet()
    {
        var previous = LiveDataEngine.TripWatchdogInterval;
        LiveDataEngine.TripWatchdogInterval = TimeSpan.FromMilliseconds(150);
        var (engine, _) = await CreateAsync();
        try
        {
            engine.KeepRunningForTrip = true;
            await engine.StartAsync();
            Assert.True(await WaitUntilAsync(() => engine.IsRunning, TimeSpan.FromSeconds(3)));

            // What Stop does: the flag first, then the forced stop.
            engine.KeepRunningForTrip = false;
            await engine.StopAsync(force: true);
            await Task.Delay(TimeSpan.FromMilliseconds(900));

            Assert.False(engine.IsRunning, "Polling came back after the trip was stopped.");
        }
        finally
        {
            engine.KeepRunningForTrip = false;
            await engine.StopAsync(force: true);
            LiveDataEngine.TripWatchdogInterval = previous;
        }
    }

    [Fact]
    public async Task WhileTheAdapterLinkIsDown_NothingIsStarted_AndWhenItIsBack_PollingResumes()
    {
        var previous = LiveDataEngine.TripWatchdogInterval;
        LiveDataEngine.TripWatchdogInterval = TimeSpan.FromMilliseconds(150);
        var (engine, sim) = await CreateAsync();
        try
        {
            engine.KeepRunningForTrip = true;
            await engine.StartAsync();
            Assert.True(await WaitUntilAsync(() => engine.IsRunning, TimeSpan.FromSeconds(3)));

            sim.Drop();
            Assert.True(await WaitUntilAsync(() => !engine.IsRunning, TimeSpan.FromSeconds(5)));
            Assert.False(engine.IsAdapterConnected);

            // A dead link: the watchdog must not hammer it with start attempts.
            await Task.Delay(TimeSpan.FromMilliseconds(900));
            Assert.False(engine.IsRunning);

            await sim.ConnectAsync(new BleDeviceItem());
            Assert.True(await WaitUntilAsync(() => engine.IsRunning, TimeSpan.FromSeconds(8)),
                "Polling did not come back after the adapter reconnected while a trip was recording.");
        }
        finally
        {
            engine.KeepRunningForTrip = false;
            await engine.StopAsync(force: true);
            LiveDataEngine.TripWatchdogInterval = previous;
        }
    }

    [Fact]
    public async Task ADisposedEngine_IsNotRevivedByTheWatchdog()
    {
        var previous = LiveDataEngine.TripWatchdogInterval;
        LiveDataEngine.TripWatchdogInterval = TimeSpan.FromMilliseconds(150);
        var (engine, _) = await CreateAsync();
        try
        {
            engine.KeepRunningForTrip = true;
            await engine.DisposeAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(900));

            Assert.False(engine.IsRunning);
        }
        finally
        {
            LiveDataEngine.TripWatchdogInterval = previous;
        }
    }

    [Fact]
    public async Task TheTripFlag_StillKeepsOtherPagesFromStoppingPolling()
    {
        // The older rule the watchdog builds on: a page leaving (a plain, non-forced stop) does not end a trip's polling.
        var (engine, _) = await CreateAsync();
        try
        {
            engine.KeepRunningForTrip = true;
            await engine.StartAsync();
            Assert.True(await WaitUntilAsync(() => engine.IsRunning, TimeSpan.FromSeconds(3)));

            await engine.StopAsync();   // not forced
            Assert.True(engine.IsRunning);
        }
        finally
        {
            engine.KeepRunningForTrip = false;
            await engine.StopAsync(force: true);
        }
    }

    // ------------------------------------------------------------------ the keep-alive's state

    [Fact]
    public void TheKeepAlive_StartsActive_AndStopClearsItAndAnyProblem()
    {
        TripKeepAlive.Stop();
        try
        {
            TripKeepAlive.Start("t", "x");
            Assert.True(TripKeepAlive.IsActive);
            Assert.Equal(TripKeepAliveProblem.None, TripKeepAlive.Problem);

            TripKeepAlive.ReportProblem(TripKeepAliveProblem.Unavailable);
            Assert.Equal(TripKeepAliveProblem.Unavailable, TripKeepAlive.Problem);

            TripKeepAlive.Stop();
            Assert.False(TripKeepAlive.IsActive);
            Assert.Equal(TripKeepAliveProblem.None, TripKeepAlive.Problem);
        }
        finally
        {
            TripKeepAlive.Stop();
        }
    }

    [Fact]
    public void ARefusal_IsVisibleAndRaisesTheEvent_AndANewStartStartsClean()
    {
        TripKeepAlive.Stop();
        var raised = 0;
        EventHandler handler = (_, _) => Interlocked.Increment(ref raised);
        TripKeepAlive.ProblemChanged += handler;
        try
        {
            TripKeepAlive.Start("t", "x");
            TripKeepAlive.ReportProblem(TripKeepAliveProblem.LocationNotAllowed);
            Assert.Equal(1, raised);

            TripKeepAlive.ReportProblem(TripKeepAliveProblem.LocationNotAllowed);   // same again: nothing new to tell
            Assert.Equal(1, raised);

            TripKeepAlive.Start("t", "x");   // a fresh start does not carry the old refusal along
            Assert.Equal(TripKeepAliveProblem.None, TripKeepAlive.Problem);
            Assert.Equal(2, raised);
        }
        finally
        {
            TripKeepAlive.ProblemChanged -= handler;
            TripKeepAlive.Stop();
        }
    }

    [Fact]
    public void ARefusalThatArrivesAfterTheTripWasStopped_IsIgnored()
    {
        TripKeepAlive.Stop();
        TripKeepAlive.ReportProblem(TripKeepAliveProblem.Unavailable);
        Assert.Equal(TripKeepAliveProblem.None, TripKeepAlive.Problem);
    }

    [Fact]
    public void AppClosed_IsRaisedOnlyWhileATripRecords()
    {
        TripKeepAlive.Stop();
        var raised = 0;
        EventHandler handler = (_, _) => Interlocked.Increment(ref raised);
        TripKeepAlive.AppClosed += handler;
        try
        {
            TripKeepAlive.NotifyAppClosed();
            Assert.Equal(0, raised);

            TripKeepAlive.Start("t", "x");
            TripKeepAlive.NotifyAppClosed();
            Assert.Equal(1, raised);
        }
        finally
        {
            TripKeepAlive.AppClosed -= handler;
            TripKeepAlive.Stop();
        }
    }

    [Fact]
    public void Recheck_NeverThrows_WhetherOrNotATripRecords()
    {
        TripKeepAlive.Stop();
        TripKeepAlive.Recheck();

        TripKeepAlive.Start("t", "x");
        try
        {
            TripKeepAlive.Recheck();
            Assert.True(TripKeepAlive.IsActive);
        }
        finally
        {
            TripKeepAlive.Stop();
        }
    }

    [Fact]
    public void WithoutAPlatform_BatteryOptimisationIsNotReported()
    {
        // Only the Android build can know; every other build must say "nothing to ask" instead of nagging.
        Assert.False(TripKeepAlive.BatteryOptimizationMayStopTrips);
        TripKeepAlive.OpenBatterySettings();   // a no-op
    }

    // ------------------------------------------------------------------ the wiring in the source files

    [Fact]
    public void TheTripService_ReportsARefusal_StopsWithTheApp_AndKeepsItsWakeLockAliveWhileReadingsArrive()
    {
        var service = Read("Platforms", "Android", "TripForegroundService.cs");
        Assert.Contains("TripKeepAlive.ReportProblem(TripKeepAliveProblem.Unavailable)", service);   // not swallowed any more
        Assert.Contains("override void OnTaskRemoved(", service);
        Assert.Contains("TripKeepAlive.NotifyAppClosed()", service);
        Assert.Contains("RenewWakeLockIfOld()", service);
        Assert.Contains("ForegroundServiceType = ForegroundService.TypeConnectedDevice", service);
        Assert.Contains("StartCommandResult.NotSticky", service);   // a killed process must not restart an empty trip
        // The service starts again at a moment the app is visible (resume / page appears), not on a loop.
        Assert.Contains("static partial void PlatformRecheck(", service);
    }

    [Fact]
    public void TheManifest_DeclaresWhatAndroid14NeedsForAConnectedDeviceService()
    {
        var manifest = Read("Platforms", "Android", "AndroidManifest.xml");
        Assert.Contains("android.permission.FOREGROUND_SERVICE\"", manifest);
        Assert.Contains("android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE", manifest);
        // Documented prerequisite on Android 14+: one of CHANGE_NETWORK_STATE / CHANGE_WIFI_STATE declared, or a
        // Bluetooth runtime permission granted. A Wi-Fi adapter user has never granted the latter.
        Assert.Contains("android.permission.CHANGE_NETWORK_STATE", manifest);
        Assert.Contains("android.permission.WAKE_LOCK", manifest);
    }

    [Fact]
    public void ThePlist_KeepsTheBackgroundModesTheKeepAliveRelaysOn()
    {
        var plist = Read("Platforms", "iOS", "Info.plist");
        Assert.Contains("<string>location</string>", plist);
        Assert.Contains("<string>bluetooth-central</string>", plist);
        Assert.Contains("NSLocationWhenInUseUsageDescription", plist);

        var ios = Read("Platforms", "iOS", "TripKeepAlive.iOS.cs");
        Assert.Contains("AllowsBackgroundLocationUpdates = true", ios);
        Assert.Contains("TripKeepAliveProblem.LocationNotAllowed", ios);   // a denied permission is reported, not silent
    }

    [Fact]
    public void Start_BringsTheBackgroundServiceUpBeforeTheSlowAdapterHandshake_AndTakesItDownAgainOnFailure()
    {
        var code = Read("ViewModels", "FuelViewModel.cs");
        var start = code.IndexOf("private async Task Start()", StringComparison.Ordinal);
        var stop = code.IndexOf("private async Task Pause()", StringComparison.Ordinal);
        Assert.True(start > 0 && stop > start);
        var body = code[start..stop];

        var keepAlive = body.IndexOf("StartKeepAlive();", StringComparison.Ordinal);
        var handshake = body.IndexOf("_engine.TryStartAsync()", StringComparison.Ordinal);
        Assert.True(keepAlive > 0 && handshake > keepAlive,
            "The foreground service must be requested at the tap, before the adapter handshake: Android refuses it once the app is hidden.");

        Assert.Contains("_engine.IsAdapterConnected && !TripKeepAlive.IsActive", body);
        Assert.Contains("keepAliveStartedHere && !tripRunning", body);
        Assert.Contains("TripKeepAlive.Stop();", body);
    }

    [Fact]
    public void ThePage_ShowsWhyABackgroundTripMayStop_AndRechecksWhenTheAppReturns()
    {
        Assert.Contains("Text=\"{Binding BackgroundNote}\" IsVisible=\"{Binding HasBackgroundNote}\"", Read("Views", "FuelPage.xaml"));

        var vm = Read("ViewModels", "FuelViewModel.cs");
        Assert.Contains("TripKeepAlive.ProblemChanged +=", vm);
        Assert.Contains("TripKeepAlive.AppClosed += OnAppClosed;", vm);
        Assert.Contains("TripKeepAliveProblem.LocationNotAllowed", vm);
        Assert.Contains("TripKeepAliveProblem.Unavailable", vm);

        Assert.Contains("TripKeepAlive.Recheck()", Read("ViewModels", "FuelViewModel.Energy.cs"));
        Assert.Contains("TripKeepAlive.Recheck()", Read("App.xaml.cs"));
    }

    [Fact]
    public void SwipingTheAppAway_SavesTheCheckpointAndPausesTheTrip()
    {
        var vm = Read("ViewModels", "FuelViewModel.cs");
        var handler = vm[vm.IndexOf("private void OnAppClosed(", StringComparison.Ordinal)..];
        Assert.True(handler.IndexOf("WriteCheckpoint();", StringComparison.Ordinal) < handler.IndexOf("await Pause();", StringComparison.Ordinal),
            "The checkpoint must be written before anything else: the process can end right after the swipe.");
    }

    [Fact]
    public void TheBatteryHint_IsShownOnceAndOnlyWhenTheAppIsStillOptimised()
    {
        var vm = Read("ViewModels", "FuelViewModel.cs");
        Assert.Contains("TripKeepAlive.BatteryOptimizationMayStopTrips", vm);
        Assert.Contains("Preferences.Default.Get(BatteryHintShownKey, false)", vm);
        Assert.Contains("Preferences.Default.Set(BatteryHintShownKey, true)", vm);
        Assert.Contains("TripKeepAlive.OpenBatterySettings()", vm);
    }

    [Fact]
    public void ThePolling_IsRestartedByAWatchdogOnlyWhileATripRecords()
    {
        var engine = Read("Services", "LiveDataEngine.cs");
        Assert.Contains("ArmTripWatchdog(value)", engine);
        Assert.Contains("LiveDataStartPolicy.Decide(IsRunning, _obd.IsInitialized, _obd.IsAdapterConnected)", engine);
        Assert.Contains("if (_disposed || !KeepRunningForTrip)", engine);
    }
}
