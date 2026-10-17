using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Tests.Simulation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// Regression tests for confirmed root causes from the administrator's crash report. They run on the software ELM327 and
/// plain .NET types; none of them proves behaviour on a phone or a car.
/// </summary>
public sealed class CrashReportingFlattenTests
{
    private static Exception Thrown(Func<Exception> make)
    {
        try { throw make(); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void AnAggregate_IsReportedByItsInnerExceptions_NotAsAnEmptyAggregate()
    {
        var inner1 = Thrown(() => new InvalidOperationException("adapter"));
        var inner2 = Thrown(() => new TimeoutException("slow"));
        var aggregate = new AggregateException(inner1, inner2);

        var leaves = CrashExceptionFlattener.Leaves(aggregate);

        Assert.Equal(new[] { typeof(InvalidOperationException), typeof(TimeoutException) }, leaves.Select(item => item.GetType()).ToArray());
        // The wrapper itself was never thrown: it has no stack. The parts do.
        Assert.True(string.IsNullOrEmpty(aggregate.StackTrace));
        Assert.All(leaves, leaf => Assert.False(string.IsNullOrWhiteSpace(leaf.StackTrace)));
    }

    [Fact]
    public void NestedAggregates_AreExpanded_AndReflectionWrappersUnwrapped()
    {
        var real = Thrown(() => new FormatException("x"));
        var nested = new AggregateException(new AggregateException(new System.Reflection.TargetInvocationException(real)));

        var leaf = Assert.Single(CrashExceptionFlattener.Leaves(nested));

        Assert.IsType<FormatException>(leaf);
    }

    [Fact]
    public void UserCancellation_IsNotAFailure()
    {
        Assert.True(CrashExceptionFlattener.IsOnlyCancellation(new OperationCanceledException()));
        Assert.True(CrashExceptionFlattener.IsOnlyCancellation(new TaskCanceledException()));
        Assert.True(CrashExceptionFlattener.IsOnlyCancellation(new AggregateException(new TaskCanceledException(), new OperationCanceledException())));
        Assert.False(CrashExceptionFlattener.IsOnlyCancellation(new AggregateException(new TaskCanceledException(), new InvalidOperationException())));
    }

    [Fact]
    public void ACancellationInsideARealFailure_DoesNotHideTheFailure()
    {
        var aggregate = new AggregateException(new OperationCanceledException(), new InvalidOperationException());

        var leaf = Assert.Single(CrashExceptionFlattener.Leaves(aggregate));

        Assert.IsType<InvalidOperationException>(leaf);
    }

    [Fact]
    public void AHugeAggregate_ProducesAtMostAFewParts()
    {
        var aggregate = new AggregateException(Enumerable.Range(0, 200).Select(i => (Exception)new InvalidOperationException($"e{i}")));

        Assert.True(CrashExceptionFlattener.Leaves(aggregate).Count <= CrashExceptionFlattener.MaxParts * 4);
    }

    [Fact]
    public void TheReport_NeverCarriesTheMessage_Paths_OrCredentials()
    {
        const string stack = "   at CarScanner.Services.Foo.Bar() in C:\\build\\src\\Foo.cs:line 42\n   at X.Y() token=abc123secret";

        var sanitized = CrashExceptionFlattener.SanitizeStack(stack);

        Assert.DoesNotContain("C:\\build", sanitized);
        Assert.DoesNotContain("abc123secret", sanitized);
        Assert.Contains("[REDACTED]", sanitized);
        Assert.Contains("CarScanner.Services.Foo.Bar()", sanitized);
        Assert.Equal(string.Empty, CrashExceptionFlattener.SanitizeStack(null));
        Assert.True(CrashExceptionFlattener.SanitizeStack(new string('a', 20_000)).Length <= 8_000);
        // only the type name is ever taken from an exception
        Assert.Equal("System.InvalidOperationException", CrashExceptionFlattener.TypeName(new InvalidOperationException("VIN 1G1RA6E49EU123456")));
    }

    [Fact]
    public void TheSameFailure_HasTheSameFingerprint_AndDifferentOnesDiffer()
    {
        var a = Thrown(() => new InvalidOperationException("one"));
        var again = Thrown(() => new InvalidOperationException("two"));
        var other = Thrown(() => new TimeoutException());

        string Print(Exception ex) => CrashExceptionFlattener.Fingerprint(CrashExceptionFlattener.TypeName(ex), CrashExceptionFlattener.SanitizeStack(ex.StackTrace));

        Assert.NotEqual(Print(a), Print(other));
        Assert.Equal(CrashExceptionFlattener.Fingerprint("T", "at A\nat B"), CrashExceptionFlattener.Fingerprint("T", "at A\nat B"));
        Assert.StartsWith("System.InvalidOperationException|", Print(again));
    }

    [Theory]
    [InlineData(true, false, "fatal:AppDomain")]
    [InlineData(false, true, "recoverable:AppDomain/aggregate")]
    [InlineData(false, false, "recoverable:AppDomain")]
    public void TheCategory_SeparatesFatalFromRecoverable_AndMarksAggregates(bool fatal, bool aggregate, string expected) =>
        Assert.Equal(expected, CrashExceptionFlattener.Category("AppDomain", fatal, aggregate));
}

public sealed class LiveDataStartTests
{
    [Theory]
    [InlineData(true, true, true, LiveDataStartDecision.AlreadyRunning)]
    [InlineData(true, false, false, LiveDataStartDecision.AlreadyRunning)]
    [InlineData(false, false, false, LiveDataStartDecision.AdapterNotConnected)]
    [InlineData(false, true, false, LiveDataStartDecision.Proceed)]
    [InlineData(false, false, true, LiveDataStartDecision.Proceed)]
    [InlineData(false, true, true, LiveDataStartDecision.Proceed)]
    public void StartDecision_FollowsTheConnectionState(bool running, bool initialized, bool connected, LiveDataStartDecision expected) =>
        Assert.Equal(expected, LiveDataStartPolicy.Decide(running, initialized, connected));

    private static OBDService NewService(SimulatedElm327 sim) =>
        new(new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance), new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);

    [Fact]
    public async Task InitializeWithoutALink_ThrowsTheTypedException_AndLeavesNothingInitialised()
    {
        var sim = new SimulatedElm327();
        var obd = NewService(sim);

        var thrown = await Assert.ThrowsAsync<AdapterNotConnectedException>(() => obd.InitializeAsync());

        Assert.IsAssignableFrom<InvalidOperationException>(thrown);   // old catch sites keep working
        Assert.False(obd.IsInitialized);
        Assert.False(obd.IsAdapterConnected);
        Assert.Empty(sim.Sent);                                        // nothing was sent to a missing adapter
    }

    [Fact]
    public async Task ADisconnectedAdapter_CanBeInitialisedLater_WithoutAStuckGate()
    {
        var sim = new SimulatedElm327();
        sim.ObdAnswers["0100"] = "41 00 00 19 00 01";
        var obd = NewService(sim);

        await Assert.ThrowsAsync<AdapterNotConnectedException>(() => obd.InitializeAsync());
        await Assert.ThrowsAsync<AdapterNotConnectedException>(() => obd.InitializeAsync());   // the gate was released
        await sim.ConnectAsync(new BleDeviceItem());
        await obd.InitializeAsync();

        Assert.True(obd.IsInitialized);
        Assert.True(obd.IsAdapterConnected);
    }

    [Fact]
    public async Task ConcurrentInitialisations_RunOneAtATime_AndBothEndInitialised()
    {
        var sim = new SimulatedElm327();
        sim.ObdAnswers["0100"] = "41 00 00 19 00 01";
        await sim.ConnectAsync(new BleDeviceItem());
        var obd = NewService(sim);

        await Task.WhenAll(obd.InitializeAsync(), obd.InitializeAsync());

        Assert.True(obd.IsInitialized);
    }

    [Fact]
    public async Task ACancelledInitialisation_IsACancellation_NotAFailure()
    {
        var sim = new SimulatedElm327();
        await sim.ConnectAsync(new BleDeviceItem());
        var obd = NewService(sim);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => obd.InitializeAsync(cts.Token));
        Assert.True(CrashExceptionFlattener.IsOnlyCancellation(new AggregateException(new TaskCanceledException())));
    }

    [Fact]
    public void TheTypedException_MapsToAFriendlyMessage_InBothLanguages()
    {
        var code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory.Split("Tests")[0], "Services", "UserError.cs"));

        Assert.Contains("AdapterNotConnectedException =>", code);
        Assert.Contains("The adapter is not connected. Connect it and try again.", code);
        Assert.Contains("ადაპტერი არ არის დაკავშირებული. დააკავშირე და სცადე თავიდან.", code);
    }
}

public sealed class NavigationGateTests
{
    private sealed class Clock
    {
        public DateTime Now = new(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        public void Advance(int ms) => Now = Now.AddMilliseconds(ms);
    }

    private static (NavigationGate Gate, Clock Clock) Create()
    {
        var clock = new Clock();
        return (new NavigationGate(() => clock.Now), clock);
    }

    [Fact]
    public void ADoubleTapOnBack_StartsOnePop_NotTwo()
    {
        var (gate, _) = Create();

        Assert.True(gate.TryBegin(".."));
        Assert.False(gate.TryBegin(".."));          // second tap while the first is still animating
    }

    [Fact]
    public void WhileOneNavigationRuns_NothingElseStarts_EvenToAnotherPage()
    {
        var (gate, _) = Create();

        Assert.True(gate.TryBegin("LiveDataPage"));
        Assert.False(gate.TryBegin("GraphStudioPage"));
        Assert.False(gate.TryBegin(".."));
        Assert.True(gate.InFlight);
    }

    [Fact]
    public void AfterItFinished_AnotherPageMayBeOpenedAtOnce()
    {
        var (gate, _) = Create();
        gate.TryBegin("LiveDataPage");
        gate.End();

        Assert.True(gate.TryBegin("GraphStudioPage"));
    }

    [Fact]
    public void TheSameTargetAgainRightAfterItFinished_IsADoubleTap_ButNotAfterTheWindow()
    {
        var (gate, clock) = Create();
        gate.TryBegin("ServicePage");
        gate.End();

        clock.Advance(300);
        Assert.False(gate.TryBegin("ServicePage"));

        clock.Advance(600);
        Assert.True(gate.TryBegin("ServicePage"));
    }

    [Fact]
    public void ANavigationThatNeverReportsBack_DoesNotBlockTheAppForEver()
    {
        var (gate, clock) = Create();
        gate.TryBegin("A");

        clock.Advance(3000);
        Assert.False(gate.TryBegin("B"));

        clock.Advance(4000);
        Assert.True(gate.TryBegin("B"));
    }

    [Fact]
    public void AFailedNavigation_ReleasesTheGate()
    {
        var (gate, clock) = Create();
        gate.TryBegin("A");
        gate.End();                                  // SafeNavigation calls End in finally, also after an exception
        clock.Advance(800);

        Assert.False(gate.InFlight);
        Assert.True(gate.TryBegin("B"));
    }

    [Theory]
    [InlineData(1, 0, false)]    // only the root page: nothing to go back to (the SendPopping consistency error)
    [InlineData(0, 0, false)]
    [InlineData(2, 0, true)]
    [InlineData(1, 1, true)]     // a modal page can be closed
    public void BackIsRefusedAtTheRoot(int stack, int modal, bool allowed) =>
        Assert.Equal(allowed, NavigationGate.CanGoBack(stack, modal));

    [Theory]
    [InlineData("..", true)]
    [InlineData(" .. ", true)]
    [InlineData("../Page", false)]
    [InlineData("//HomePage", false)]
    public void OnlyThePlainBackRouteIsABackRequest(string route, bool back) =>
        Assert.Equal(back, NavigationGate.IsBack(route));

    [Fact]
    public async Task ManyRapidRequests_LetExactlyOnePassPerRound()
    {
        var (gate, _) = Create();
        var started = 0;

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => { if (gate.TryBegin("..")) Interlocked.Increment(ref started); })));

        Assert.Equal(1, started);
    }

    [Fact]
    public void NoCodeOutsideTheWrapper_NavigatesThroughShellDirectly()
    {
        var root = AppContext.BaseDirectory.Split("Tests")[0];
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}") &&
                           !path.EndsWith("SafeNavigation.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("Shell.Current.GoToAsync(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, "direct Shell navigation in: " + string.Join(", ", offenders));
    }
}

public sealed class ServiceUiThreadTests
{
    private static string ViewModelSource() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory.Split("Tests")[0], "ViewModels", "AdvancedViewModels.cs"));

    [Theory]
    [InlineData("private void ClearOperationLists()", "OnUiThreadOrQueue(ClearOperationLists)")]
    [InlineData("private void UpdateCollectionState()", "OnUiThreadOrQueue(UpdateCollectionState)")]
    [InlineData("private void SetErrorState(Exception exception)", "OnUiThreadOrQueue(() => SetErrorState(exception))")]
    public void TheServiceListsAreOnlyChangedOnTheUiThread(string method, string guard)
    {
        var code = ViewModelSource();
        var body = code[code.IndexOf(method, StringComparison.Ordinal)..];
        body = body[..Math.Min(body.Length, 400)];

        Assert.Contains(guard, body);
    }

    [Fact]
    public void TheGuardQueuesInsteadOfBlocking()
    {
        var code = ViewModelSource();
        var helper = code[code.IndexOf("private static bool OnUiThreadOrQueue(Action action)", StringComparison.Ordinal)..];
        helper = helper[..300];

        Assert.Contains("BeginInvokeOnMainThread", helper);
        Assert.DoesNotContain("InvokeOnMainThreadAsync", helper);
        Assert.DoesNotContain(".Wait(", helper);
        Assert.DoesNotContain(".Result", helper);
    }
}

/// <summary>
/// Android native-lifecycle guards. They read the sources: the Android types cannot run in a unit test, so these show only
/// that the protective structure is present, not that it works on a device.
/// </summary>
public sealed class AndroidNativeGuardTests
{
    private static string Root => AppContext.BaseDirectory.Split("Tests")[0];

    [Fact]
    public void TheTextureViewListener_HasTheActivationConstructor_AndHoldsTheControllerWeakly()
    {
        var code = File.ReadAllText(Path.Combine(Root, "Platforms", "Android", "PerformanceVideo", "AndroidPerformanceCameraController.cs"));
        var listener = code[code.IndexOf("private sealed class SurfaceListener", StringComparison.Ordinal)..];

        Assert.Contains("public SurfaceListener(IntPtr handle, JniHandleOwnership transfer)", listener);
        Assert.Contains("WeakReference<AndroidPerformanceCameraController>", listener);
        Assert.Contains("_view.SurfaceTextureListener = _listener;", code);
        Assert.DoesNotContain("_view.SurfaceTextureListener = this;", code);
    }

    [Fact]
    public void TheCameraController_IsNeverDisposed_OnlyReleased()
    {
        var handler = File.ReadAllText(Path.Combine(Root, "Platforms", "Android", "PerformanceVideo", "PerformanceCameraViewHandler.cs"));

        Assert.Contains("_controller?.Release();", handler);
        Assert.DoesNotContain("_controller?.Dispose()", handler);
    }

    [Fact]
    public void TheManifest_DoesNotRedeclareMainActivity_SoItKeepsItsMauiTheme()
    {
        var manifest = File.ReadAllText(Path.Combine(Root, "Platforms", "Android", "AndroidManifest.xml"));
        var activity = File.ReadAllText(Path.Combine(Root, "Platforms", "Android", "MainActivity.cs"));

        // A hand-written <activity> for MainActivity would drop the generated theme and break AppBarLayout inflation
        // ("The style on this component requires your app theme to be Theme.AppCompat").
        Assert.DoesNotContain("<activity ", manifest.Replace("<activity-alias", string.Empty), StringComparison.Ordinal);
        Assert.Contains("Theme = \"@style/Maui.SplashTheme\"", activity);
        Assert.Contains("class MainActivity : MauiAppCompatActivity", activity);
    }

    [Fact]
    public void NoNativeAppBarLayout_IsInflatedByTheApp()
    {
        var offenders = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xml", StringComparison.Ordinal) || path.EndsWith(".axml", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}Backend{Path.DirectorySeparatorChar}"))
            .Where(path => File.ReadAllText(path).Contains("AppBarLayout", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(Root, path))
            // the manifest comment that explains the rule is allowed to name it
            .Where(path => !path.EndsWith("AndroidManifest.xml", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0, "app code that names AppBarLayout: " + string.Join(", ", offenders));
    }
}

/// <summary>
/// Regression tests for the reported freeze where rapid page entry/exit — especially starting a connection and leaving
/// right away — left the app on a blank screen that only a force-kill recovered from. Root cause: a connect attempt can
/// block forever in the native BLE/classic-socket call (the classic socket's Connect() ignores cancellation once it has
/// started), which held BluetoothService's own connect gate, and the ObdConnectionService gate one layer up, for ever —
/// so every later connect AND every disconnect (it needs the same gate) hung too. BluetoothService.cs has heavy MAUI/BLE
/// dependencies and cannot run in a unit test, so these read the source to confirm the bounding structure is present.
/// </summary>
public sealed class BluetoothConnectTimeoutTests
{
    private static string Source() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory.Split("Tests")[0], "Services", "Bluetooth", "BluetoothService.cs"));

    [Fact]
    public void ConnectAsync_DelegatesToTheBoundedHelper_InsteadOfAwaitingConnectCoreDirectly()
    {
        var code = Source();
        var method = code[code.IndexOf("public async Task ConnectAsync(", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("private async Task ConnectCoreBoundedAsync(", StringComparison.Ordinal)];

        Assert.Contains("ConnectCoreBoundedAsync(item, ConnectTimeout, cancellationToken)", method);
        // A plain "await ConnectCoreAsync(...)" with no race would reintroduce the hang this guards against.
        Assert.DoesNotContain("await ConnectCoreAsync(", method);
    }

    [Fact]
    public void ConnectCoreBoundedAsync_RacesTheNativeCallAgainstATimeBudget_InsteadOfAwaitingItDirectly()
    {
        var code = Source();
        var method = code[code.IndexOf("private async Task ConnectCoreBoundedAsync(", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("\n    private void ObserveAbandonedConnectAttempt", StringComparison.Ordinal)];

        Assert.Contains("Task.WhenAny(connectTask, delayTask)", method);
        Assert.Contains("Task.Delay(timeout)", method);
        Assert.DoesNotContain("await ConnectCoreAsync(", method);
    }

    [Fact]
    public void StartAutoReconnect_AlsoGoesThroughTheBoundedHelper_NotConnectCoreDirectly()
    {
        // Regression for a real gap: the retry loop used to await ConnectCoreAsync directly, so a
        // reconnect attempt that hit the same stuck-native-Connect() risk as a manual connect would
        // hold _connectGate forever — silently killing this loop AND blocking the user from ever
        // manually reconnecting again.
        var code = Source();
        var method = code[code.IndexOf("private void StartAutoReconnect()", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("private void DetachCharacteristics()", StringComparison.Ordinal)];

        Assert.Contains("ConnectCoreBoundedAsync(device, TimeSpan.FromSeconds(12), CancellationToken.None)", method);
        Assert.DoesNotContain("await ConnectCoreAsync(", method);
    }

    [Fact]
    public void AnAbandonedConnectAttempt_IsObserved_NotLeftToCrashAsUnobserved()
    {
        var code = Source();
        Assert.Contains("ObserveAbandonedConnectAttempt(connectTask, item, linkedSource, timeoutSource)", code);

        var observer = code[code.IndexOf("private void ObserveAbandonedConnectAttempt(", StringComparison.Ordinal)..];
        observer = observer[..Math.Min(observer.Length, 900)];
        Assert.Contains("ContinueWith(", observer);
    }

    [Fact]
    public void DisconnectClassicAsync_NeverAwaitsTheReadLoopUnbounded()
    {
        var code = Source();
        var method = code[code.IndexOf("private async Task DisconnectClassicAsync()", StringComparison.Ordinal)..];
        method = method[..Math.Min(method.Length, 2600)];

        Assert.Contains("Task.WhenAny(_classicReadTask, Task.Delay(", method);
        Assert.DoesNotContain("await _classicReadTask\n", method);
    }

    [Fact]
    public void AutoReconnect_NeverPermanentlyGivesUp()
    {
        // User report, 2026-10-17: a real BLE link drop while driving outlasted the old fixed 7-attempt backoff
        // (~52s total), and the app then needed a manual reconnect even though the adapter would have come back
        // on its own. StartAutoReconnect's loop must keep retrying (at the longest backoff step) until it
        // reconnects, the user disconnects, or the service is disposed — never fall out of the loop on its own.
        var code = Source();
        var method = code[code.IndexOf("private void StartAutoReconnect()", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("private void DetachCharacteristics()", StringComparison.Ordinal)];

        Assert.Contains("for (var round = 0; ; round++)", method);
        Assert.DoesNotContain("foreach (var seconds in new[]", method);
    }

    [Fact]
    public void ClassicConnect_BumpsAGeneration_AfterTearingDownTheOldSession()
    {
        // The read loop's one native read cannot be reliably cancelled (see DisconnectClassicAsync's own
        // comment), so a prior connection's reader can still be running when a new one starts. Without a
        // generation to compare against, a late answer from that old reader could complete the new
        // session's pending command or declare the new session's link lost on the old session's behalf.
        var code = Source();
        var method = code[code.IndexOf("private async Task ConnectClassicAsync(", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("private static async Task EnsureClassicPairingAsync", StringComparison.Ordinal)];

        Assert.Contains("await DisconnectClassicAsync()", method);
        Assert.Contains("Volatile.Read(ref _classicGeneration)", method);
        Assert.Contains("ClassicReadLoopAsync(", method);
        Assert.Contains("_classicReadCts.Token,", method);
        Assert.Contains("generation));", method);

        // The actual bump lives in DisconnectClassicAsync, not here — so that every teardown retires
        // the outgoing reader unconditionally, not only a classic-to-classic reconnect.
        var disconnect = code[code.IndexOf("private async Task DisconnectClassicAsync()", StringComparison.Ordinal)..];
        disconnect = disconnect[..Math.Min(disconnect.Length, 2600)];
        Assert.Contains("Interlocked.Increment(ref _classicGeneration)", disconnect);
    }

    [Fact]
    public void ClassicReadLoop_NeverTouchesSharedStateOnceSupersededByANewerConnection()
    {
        var code = Source();
        var method = code[code.IndexOf("private async Task ClassicReadLoopAsync(", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("private async Task HandleClassicLinkLostAsync()", StringComparison.Ordinal)];

        Assert.Contains("int generation", method);
        Assert.Contains("Volatile.Read(ref _classicGeneration) != generation", method);
        // Checked both on the success path (after a read returns) and in the catch block, before any
        // _pendingResponse/HandleClassicLinkLostAsync call — not only at the very top of the loop, since
        // the whole point is a read that is already in flight when superseded.
        Assert.Contains("if (Retired())", method);
    }

    [Fact]
    public void DisconnectClassicAsync_FailsAnyPendingCommandImmediately_InsteadOfLeavingItToTimeOut()
    {
        var code = Source();
        var method = code[code.IndexOf("private async Task DisconnectClassicAsync()", StringComparison.Ordinal)..];
        method = method[..Math.Min(method.Length, 2600)];

        Assert.Contains("_pendingResponse?.TrySetException(", method);
    }
}

/// <summary>
/// Regression test for a real Chevrolet Volt 2013 test on the admin-only Research page: ignition and engine on,
/// yet every module came back "did not answer" (sent 11, positive 0, no reply 11, skipped 162). Root cause:
/// the discovery loop wrote a module off as silent after its very first unanswered DID and never tried that
/// module's other, possibly-working DIDs again — safe for the already-reviewed (debug=0) catalog, but research
/// (debug=1) catalogs such as OBDb's Volt repo share one signal file across two very different generations
/// (2011-2015 vs 2016-2019, per generations.yaml), so the first DID tried for a module is easily one that exact
/// car's generation never supported. ModuleLiveDataService.cs has heavy MAUI/SQLite dependencies and cannot run
/// in a unit test, so this reads the source to confirm the retry-budget structure is present.
/// </summary>
public sealed class ModuleLiveDataResearchRetryTests
{
    private static string Source() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory.Split("Tests")[0], "Services", "OemDiagnostics", "ModuleLiveDataService.cs"));

    [Fact]
    public void ResearchMode_GivesAModuleSeveralMissesBeforeWritingItOffAsSilent()
    {
        var code = Source();
        var method = code[code.IndexOf("await _obd.RunDtcModuleSessionAsync(async session =>", StringComparison.Ordinal)..];
        method = method[..Math.Min(method.Length, 2600)];

        Assert.Contains("var maxMissesPerModule = debugOnly ? 5 : 1;", method);
        Assert.Contains("misses.GetValueOrDefault(module) + 1", method);
        Assert.Contains("if (count >= maxMissesPerModule)", method);
        // A reply of any kind (including a negative response code) un-silences the module for its own later DIDs.
        Assert.Contains("misses.Remove(module)", method);
    }

    [Fact]
    public void TheAlreadyReviewedCatalog_KeepsTheOriginalOneMissBehaviour()
    {
        // debugOnly defaults to false, so every existing caller (the normal Module Live Data page) is unaffected.
        var code = Source();
        Assert.Contains("bool debugOnly = false", code);
    }
}

/// <summary>
/// Regression test for admin crowd-sourced captures (Chevrolet Volt, Ford Escape, Toyota Prius, 2026-10-16): the
/// exact same standard PID answered "valid" on one read and "no data"/timeout moments later with a newly bought
/// adapter. Root cause: OBDService.DiscoverSupportedPidsAsync asked each of its 7 support-bitmap blocks with zero
/// retries, so one missed block broke the whole scan and cached whatever few PIDs it had found as final for the
/// rest of the connected session (DiscoverSupportedPidsAsync returns the cache immediately once it is non-empty)
/// — the only recovery was a full disconnect/reconnect, matching "All Sensors showed almost nothing until I
/// reconnected the adapter" and the Prius showing "0 information" despite the car answering plenty of PIDs.
/// </summary>
public sealed class SupportedPidDiscoveryRetryTests
{
    private static async Task<OBDService> CreateAsync(Action<SimulatedElm327> configure)
    {
        var sim = new SimulatedElm327();
        configure(sim);
        var elm = new Elm327Protocol(sim, NullLogger<Elm327Protocol>.Instance);
        var obd = new OBDService(elm, new PIDDecoder(), new DTCDecoder(), sim, NullLogger<OBDService>.Instance);
        await sim.ConnectAsync(new BleDeviceItem());
        await obd.InitializeAsync();
        return obd;
    }

    [Fact]
    public async Task ATransientTimeoutOnOneSupportBlock_NoLongerBreaksTheWholeDiscovery()
    {
        // "0100" (the very first of the 7 support-bitmap blocks) times out once, exactly like the real adapter
        // did, then answers normally — simulating the retry this fix adds, not a persistently broken ECU.
        var obd = await CreateAsync(s => s.TimeoutOnNextSend.Add("0100"));

        // PID 0x0C (RPM) is advertised by the default "0100" bitmap (BE 3F A8 13) and decodable — reaching it
        // proves the block's PIDs were actually recorded, not just that discovery limped past the miss.
        Assert.True(obd.IsPidSupported(0x0C));
        Assert.NotEmpty(obd.SupportedPids);
    }
}
