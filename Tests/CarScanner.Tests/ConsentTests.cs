using System.Globalization;
using System.Text.RegularExpressions;
using CarScanner.Services.DataCollection;
using CarScanner.Services.OBD;

namespace CarScanner.Tests;

internal sealed class MemoryConsentStorage : IConsentStorage
{
    private readonly Dictionary<string, object> _values = new();
    public bool GetBool(string key, bool fallback) => _values.TryGetValue(key, out var v) && v is bool b ? b : fallback;
    public void SetBool(string key, bool value) => _values[key] = value;
    public long GetLong(string key, long fallback) => _values.TryGetValue(key, out var v) && v is long l ? l : fallback;
    public void SetLong(string key, long value) => _values[key] = value;
}

internal sealed class TestClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    public DateTimeOffset Read() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>The consent state model. The clock is injected: nothing here waits for real time.</summary>
public sealed class ConsentPolicyTests
{
    private readonly MemoryConsentStorage _storage = new();
    private readonly TestClock _clock = new();

    // A new instance over the same storage is "the app after a restart or a reboot".
    private ConsentPolicy Policy() => new(_storage, _clock.Read);

    [Fact]
    public void FirstTimeUser_HasNoDecision_AndIsAsked()
    {
        var p = Policy();
        Assert.Equal(ConsentState.NoDecision, p.State);
        Assert.True(p.ShouldPrompt);
        Assert.False(p.IsGranted);
    }

    [Fact]
    public void Accepting_Persists_AcrossRestarts_AndIsNeverAskedAgain()
    {
        Policy().Grant();

        foreach (var later in new[] { TimeSpan.FromMinutes(1), TimeSpan.FromHours(25), TimeSpan.FromDays(400) })
        {
            _clock.Advance(later);
            var restarted = Policy();
            Assert.Equal(ConsentState.Granted, restarted.State);
            Assert.True(restarted.IsGranted);
            Assert.False(restarted.ShouldPrompt);
        }
    }

    [Fact]
    public void Granting_RecordsAUtcTimestamp_AndKeepsTheFirstOne()
    {
        var first = _clock.Now;
        Policy().Grant();
        _clock.Advance(TimeSpan.FromDays(3));
        Policy().Grant();

        var p = Policy();
        Assert.Equal(first, p.GrantedAt);
        Assert.Equal(TimeSpan.Zero, p.GrantedAt!.Value.Offset);
    }

    [Fact]
    public void Declining_SuppressesThePrompt_ForTwentyFourHours()
    {
        Policy().Decline();

        Assert.Equal(ConsentState.DeclinedWaiting, Policy().State);
        Assert.False(Policy().ShouldPrompt);

        _clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59));
        Assert.Equal(ConsentState.DeclinedWaiting, Policy().State);
        Assert.False(Policy().ShouldPrompt);
    }

    [Fact]
    public void ADecline_IsFollowedByAnotherPrompt_AfterTwentyFourHours()
    {
        Policy().Decline();
        _clock.Advance(ConsentPolicy.DeclineWait);

        var p = Policy();
        Assert.Equal(ConsentState.NoDecision, p.State);
        Assert.True(p.ShouldPrompt);
    }

    [Fact]
    public void Declining_Persists_AcrossRestarts()
    {
        Policy().Decline();
        _clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(ConsentState.DeclinedWaiting, Policy().State);
        Assert.NotNull(Policy().DeclinedAt);
    }

    [Fact]
    public void DecliningAgain_RestartsTheWait()
    {
        Policy().Decline();
        _clock.Advance(TimeSpan.FromHours(25));
        Assert.True(Policy().ShouldPrompt);

        Policy().Decline();                       // declined a second time, 25 h after the first
        _clock.Advance(TimeSpan.FromHours(23));
        Assert.False(Policy().ShouldPrompt);       // 48 h after the first decline, but only 23 h after the second
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(Policy().ShouldPrompt);
    }

    [Fact]
    public void ADecline_IsNeverConsent()
    {
        var p = Policy();
        p.Decline();
        Assert.False(p.IsGranted);
        Assert.False(_storage.GetBool(ConsentPolicy.GrantedKey, false));

        _clock.Advance(TimeSpan.FromDays(30));
        Assert.False(Policy().IsGranted);
    }

    [Fact]
    public void Withdrawing_StopsConsent_AndTheFirstRunQuestionDoesNotComeBack()
    {
        Policy().Grant();
        _clock.Advance(TimeSpan.FromDays(2));
        Policy().Revoke();

        var p = Policy();
        Assert.Equal(ConsentState.Revoked, p.State);
        Assert.False(p.IsGranted);
        Assert.False(p.ShouldPrompt);
        Assert.NotNull(p.RevokedAt);

        _clock.Advance(TimeSpan.FromDays(365));
        Assert.Equal(ConsentState.Revoked, Policy().State);
        Assert.False(Policy().ShouldPrompt);
    }

    [Fact]
    public void WithdrawingWithoutAGrant_ChangesNothing()
    {
        Policy().Revoke();
        Assert.Equal(ConsentState.NoDecision, Policy().State);
        Assert.Null(Policy().RevokedAt);
    }

    [Fact]
    public void ConsentCanBeGivenAgainInSettings_AfterWithdrawalOrADecline()
    {
        Policy().Decline();
        Policy().Grant();
        Assert.Equal(ConsentState.Granted, Policy().State);

        Policy().Revoke();
        Assert.Equal(ConsentState.Revoked, Policy().State);

        _clock.Advance(TimeSpan.FromHours(1));
        Policy().Grant();
        Assert.Equal(ConsentState.Granted, Policy().State);
    }

    [Fact]
    public void ADeclineWhileGranted_IsIgnored()
    {
        Policy().Grant();
        Policy().Decline();
        Assert.Equal(ConsentState.Granted, Policy().State);
        Assert.Null(Policy().DeclinedAt);
    }

    [Fact]
    public void ConsentGivenBeforeTimestampsExisted_StillCounts()
    {
        _storage.SetBool(ConsentPolicy.GrantedKey, true);     // the old Settings checkbox, no dates
        var p = Policy();
        Assert.Equal(ConsentState.Granted, p.State);
        Assert.False(p.ShouldPrompt);
        Assert.Null(p.GrantedAt);
    }

    [Fact]
    public void ADeviceClockMovedBack_DoesNotBlockThePromptForever()
    {
        Policy().Decline();
        _clock.Advance(TimeSpan.FromDays(-5));
        Assert.Equal(ConsentState.NoDecision, Policy().State);
    }

    [Theory]
    [InlineData(-5L)]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    public void DamagedTimestamps_AreTreatedAsAbsent(long ticks)
    {
        _storage.SetLong(ConsentPolicy.DeclinedAtKey, ticks);
        Assert.Equal(ConsentState.NoDecision, Policy().State);
        Assert.Null(Policy().DeclinedAt);
    }
}

/// <summary>When the question is shown: once until answered, 24 h after a decline, never twice at once.</summary>
public sealed class ConsentPromptCoordinatorTests
{
    private readonly MemoryConsentStorage _storage = new();
    private readonly TestClock _clock = new();

    private sealed class Rig
    {
        public ConsentPromptCoordinator Coordinator = null!;
        public int Shown;
        public int GrantedCalls;
        public bool CanShow = true;
        public ConsentChoice? Answer = ConsentChoice.Agree;
        public TaskCompletionSource<ConsentChoice?>? Gate;
        public Exception? ShowFails;
        public Exception? GrantFails;
    }

    private Rig NewApp(ConsentChoice? answer = ConsentChoice.Agree)
    {
        var rig = new Rig { Answer = answer };
        var policy = new ConsentPolicy(_storage, _clock.Read);
        rig.Coordinator = new ConsentPromptCoordinator(
            policy,
            () => rig.CanShow,
            async () =>
            {
                Interlocked.Increment(ref rig.Shown);
                if (rig.ShowFails is not null) throw rig.ShowFails;
                return rig.Gate is not null ? await rig.Gate.Task : rig.Answer;
            },
            () =>
            {
                rig.GrantedCalls++;
                return rig.GrantFails is null ? Task.CompletedTask : throw rig.GrantFails;
            });
        return rig;
    }

    [Fact]
    public async Task FirstTimeUser_SeesTheModal()
    {
        var app = NewApp();
        Assert.Equal(ConsentPromptOutcome.Granted, await app.Coordinator.CheckAsync());
        Assert.Equal(1, app.Shown);
    }

    [Fact]
    public async Task AcceptedConsent_TurnsCollectionOn_AndIsNotAskedAgain_AfterRestarts()
    {
        var first = NewApp();
        await first.Coordinator.CheckAsync();
        Assert.Equal(1, first.GrantedCalls);

        for (var launch = 0; launch < 3; launch++)
        {
            _clock.Advance(TimeSpan.FromHours(30));
            var restarted = NewApp();
            Assert.Equal(ConsentPromptOutcome.NotNeeded, await restarted.Coordinator.CheckAsync());
            Assert.Equal(0, restarted.Shown);
        }
    }

    [Fact]
    public async Task NotNow_EnablesNothing_AndSuppressesTheModalForTwentyFourHours()
    {
        var app = NewApp(ConsentChoice.NotNow);
        Assert.Equal(ConsentPromptOutcome.Declined, await app.Coordinator.CheckAsync());
        Assert.Equal(0, app.GrantedCalls);

        _clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal(ConsentPromptOutcome.NotNeeded, await NewApp(ConsentChoice.NotNow).Coordinator.CheckAsync());
        Assert.Equal(1, app.Shown);
    }

    [Fact]
    public async Task AfterTwentyFourHours_TheModalReturns_AndAnotherDeclineRestartsTheWait()
    {
        await NewApp(ConsentChoice.NotNow).Coordinator.CheckAsync();

        _clock.Advance(TimeSpan.FromHours(24));
        var second = NewApp(ConsentChoice.NotNow);
        Assert.Equal(ConsentPromptOutcome.Declined, await second.Coordinator.CheckAsync());
        Assert.Equal(1, second.Shown);

        _clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal(ConsentPromptOutcome.NotNeeded, await NewApp().Coordinator.CheckAsync());

        _clock.Advance(TimeSpan.FromHours(1));
        var third = NewApp();
        Assert.Equal(ConsentPromptOutcome.Granted, await third.Coordinator.CheckAsync());
    }

    [Fact]
    public async Task ManyLifecycleEvents_NeverShowMoreThanOneModal()
    {
        var app = NewApp();
        app.Gate = new TaskCompletionSource<ConsentChoice?>();

        var first = app.Coordinator.CheckAsync();                       // launch: the modal is on screen
        var others = Enumerable.Range(0, 8).Select(_ => app.Coordinator.CheckAsync()).ToArray();   // resumes meanwhile
        var results = await Task.WhenAll(others);

        Assert.All(results, r => Assert.Equal(ConsentPromptOutcome.Busy, r));
        Assert.Equal(1, app.Shown);

        app.Gate.SetResult(ConsentChoice.NotNow);
        Assert.Equal(ConsentPromptOutcome.Declined, await first);

        // Further resumes within the 24 h wait show nothing.
        for (var i = 0; i < 5; i++)
            Assert.Equal(ConsentPromptOutcome.NotNeeded, await app.Coordinator.CheckAsync());
        Assert.Equal(1, app.Shown);
    }

    [Fact]
    public async Task WhenTheModalCannotBeShownNow_NothingIsRecorded()
    {
        var app = NewApp();
        app.CanShow = false;
        Assert.Equal(ConsentPromptOutcome.NotReady, await app.Coordinator.CheckAsync());
        Assert.Equal(0, app.Shown);

        app.CanShow = true;                                             // later, on a quiet screen
        Assert.Equal(ConsentPromptOutcome.Granted, await app.Coordinator.CheckAsync());
    }

    [Fact]
    public async Task AModalThatCouldNotBeShown_IsNotADecline()
    {
        var app = NewApp(answer: null);
        Assert.Equal(ConsentPromptOutcome.NotReady, await app.Coordinator.CheckAsync());
        Assert.Equal(0, app.GrantedCalls);

        var again = NewApp();
        Assert.Equal(ConsentPromptOutcome.Granted, await again.Coordinator.CheckAsync());   // asked again at once
    }

    [Fact]
    public async Task AFailureWhileShowing_DoesNotLeaveTheCoordinatorStuck()
    {
        var app = NewApp();
        app.ShowFails = new InvalidOperationException("ui failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Coordinator.CheckAsync());

        app.ShowFails = null;
        Assert.Equal(ConsentPromptOutcome.Granted, await app.Coordinator.CheckAsync());
    }

    [Fact]
    public async Task AFailureTurningCollectionOn_KeepsTheConsent_AndDoesNotCrash()
    {
        var app = NewApp();
        app.GrantFails = new HttpRequestException("backend unavailable");
        Assert.Equal(ConsentPromptOutcome.Granted, await app.Coordinator.CheckAsync());
        Assert.True(new ConsentPolicy(_storage, _clock.Read).IsGranted);
    }
}

/// <summary>Collection and upload follow the consent, and nothing else can switch them on.</summary>
public sealed class ConsentGatedCollectionTests : IDisposable
{
    private static readonly AsyncLocal<bool> Mine = new();     // the exchange tap is static, see CollectionBackendContractTests
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gcar-consent-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryConsentStorage _storage = new();
    private readonly TestClock _clock = new();
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private (ConsentPolicy Policy, CollectionPipeline Pipe, CollectionQueue Queue, FakeUploader Up) Create()
    {
        var policy = new ConsentPolicy(_storage, _clock.Read);
        var queue = new CollectionQueue(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json"));
        var up = new FakeUploader();
        // exactly how DataCollectionService wires it: the pipeline asks the policy on every step
        var pipe = new CollectionPipeline(queue, () => policy.IsGranted, () => new VehicleSnapshot("CHEVROLET", "VOLT", "2013", "ISO 15765-4"), up, _clock.Read);
        _disposables.Add(pipe);
        return (policy, pipe, queue, up);
    }

    private static ObdRawExchange Module(string request = "22000D", string response = "7E80462000D1A") => new(0x7E0, 0x7E8, false, request, response);
    private static ObdRawExchange Std(string request, string response) => new(0x7DF, 0, false, request, response, ObdExchangeOrigin.StandardObd, true);

    [Fact]
    public async Task BeforeConsent_NothingIsCollected_AndNothingIsUploaded()
    {
        var (_, pipe, queue, up) = Create();

        pipe.Observe(Module());
        pipe.Observe(Std("0100", "41 00 BE 3F A8 13"));
        await pipe.DrainAsync();

        Assert.Equal(0, queue.Count);
        Assert.Equal(0, await pipe.FlushAsync(CancellationToken.None, force: true));
        Assert.Equal(0, up.UploadCalls);
    }

    [Fact]
    public async Task ADecline_CollectsAndUploadsNothing()
    {
        var (policy, pipe, queue, up) = Create();
        policy.Decline();

        pipe.Observe(Module());
        await pipe.DrainAsync();
        await pipe.FlushAsync(CancellationToken.None, force: true);

        Assert.Equal(0, queue.Count);
        Assert.Equal(0, up.UploadCalls);
    }

    [Fact]
    public async Task AfterConsent_ObservationsAreCollectedAndUploadedOnce()
    {
        var (policy, pipe, queue, up) = Create();
        policy.Grant();

        pipe.Observe(Module());
        pipe.Observe(Module());                       // the same observation again
        await pipe.DrainAsync();
        Assert.Equal(1, queue.Count);

        Assert.Equal(1, await pipe.FlushAsync(CancellationToken.None, force: true));
        Assert.Single(up.Uploaded);
        Assert.Equal(0, await pipe.FlushAsync(CancellationToken.None, force: true));   // nothing is sent twice
    }

    [Fact]
    public async Task Withdrawing_StopsCollection_ClearsTheQueue_AndAsksTheServerToDelete()
    {
        var (policy, pipe, queue, up) = Create();
        policy.Grant();
        pipe.Observe(Module());
        await pipe.DrainAsync();
        Assert.Equal(1, queue.Count);

        policy.Revoke();                                   // what SetEnabledAsync(false) does first
        await pipe.DisableAsync(CancellationToken.None);

        Assert.Equal(0, queue.Count);
        Assert.Equal(1, up.Deletes);
        pipe.Observe(Module("22002F", "7E80462002F72"));
        await pipe.DrainAsync();
        Assert.Equal(0, queue.Count);
        await pipe.FlushAsync(CancellationToken.None, force: true);
        Assert.Equal(0, up.UploadCalls);
    }

    [Fact]
    public async Task AnUnreachableBackend_KeepsTheQueue_AndNeverThrows()
    {
        var (policy, pipe, queue, up) = Create();
        policy.Grant();
        up.Throw = true;
        pipe.Observe(Module());
        await pipe.DrainAsync();

        var sent = await pipe.FlushAsync(CancellationToken.None, force: true);   // must not throw

        Assert.Equal(0, sent);
        Assert.Equal(1, queue.Count);
        Assert.True(pipe.ConsecutiveFailures > 0);

        up.Throw = false;                                                         // the server comes back
        Assert.Equal(1, await pipe.FlushAsync(CancellationToken.None, force: true));
        Assert.Single(up.Uploaded);                                               // exactly one copy after the retry
    }

    [Theory]
    [InlineData("0902")]       // VIN
    [InlineData("22F190")]     // VIN identifier
    [InlineData("22F18C")]     // serial number
    [InlineData("2E060001")]   // a write
    public async Task EvenWithConsent_IdentifiersAndWritesAreNeverQueued(string request)
    {
        var (policy, pipe, queue, _) = Create();
        policy.Grant();

        pipe.Observe(Module(request, "7E80562F1900102"));
        await pipe.DrainAsync();

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task ManualCapture_StaysLocal_EvenWithConsent_AndNeverFeedsTheUploader()
    {
        var (policy, pipe, queue, up) = Create();
        var capture = new LocalCaptureSession();
        capture.Start(null, () => new CaptureVehicle("CHEVROLET", "VOLT", "2013", "CAN"), _clock.Read);

        // consent NOT given: the recording still works, the pipeline stays empty
        capture.Observe(Module());
        pipe.Observe(Module());
        await pipe.DrainAsync();
        Assert.Equal(1, capture.ItemCount);
        Assert.Equal(0, queue.Count);

        // consent given: exporting or sharing the file does not upload it
        policy.Grant();
        var json = capture.BuildJson("1.0.0");
        Assert.NotEmpty(json);
        await pipe.FlushAsync(CancellationToken.None, force: true);
        Assert.Equal(0, up.UploadCalls);
        Assert.Empty(up.Uploaded);
    }

    [Fact]
    public void TheManualCaptureCode_HasNoPathToTheNetworkOrTheUploadQueue()
    {
        var root = RepositoryRoot();
        foreach (var file in new[] { "Services/DataCollection/LocalCaptureSession.cs", "Services/DataCollection/LocalCaptureService.cs" })
        {
            var source = File.ReadAllText(Path.Combine(root, file));
            foreach (var forbidden in new[] { "ICollectionUploader", "CollectionPipeline", "CollectionQueue", "HttpClient", "PostAsync", "SendAsync(" })
                Assert.DoesNotContain(forbidden, source);
        }
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    // ---- the wiring that cannot run without a device ---------------------------------------------

    [Fact]
    public void TheCollectionService_UsesTheConsentPolicy_AsItsOnlySwitch()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Services/DataCollection/DataCollectionService.cs"));
        Assert.Contains("public bool IsEnabled => Consent.IsGranted;", source);
        Assert.DoesNotContain("Preferences.Default.Set(ConsentKey", source);   // no second way to switch collection on
        Assert.Contains("Consent.Grant();", source);
        Assert.Contains("Consent.Revoke();", source);
        Assert.Contains("() => IsEnabled", source);                            // the pipeline asks on every step
    }

    [Fact]
    public void TheQuestion_IsAskedAtLaunchAndOnResume_Only()
    {
        var app = File.ReadAllText(Path.Combine(RepositoryRoot(), "App.xaml.cs"));
        Assert.Equal(2, Regex.Matches(app, @"^\s+ScheduleConsentCheck\(\);", RegexOptions.Multiline).Count);   // Created + Resumed

        var service = File.ReadAllText(Path.Combine(RepositoryRoot(), "Services/DataCollection/ConsentPromptService.cs"));
        Assert.Contains("ModalStack.Count == 0", service);
        Assert.Contains("TripKeepAlive.IsActive", service);
        Assert.Contains("collection.SetEnabledAsync(true)", service);
    }

    [Fact]
    public void TheModal_UsesTheAppTheme_NotFixedColours()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "Views/ConsentPage.cs"));
        foreach (var key in new[] { "\"CardBg\"", "\"TextColor\"", "\"SubTextColor\"", "\"Primary\"", "\"BorderColor\"" })
            Assert.Contains(key, page);
        Assert.Contains("SetDynamicResource", page);
        Assert.Contains("OnBackButtonPressed", page);     // back = Not now, never consent
    }
}

/// <summary>The words: every language of the app, dark/light independent, accurate and complete.</summary>
public sealed class ConsentTextTests
{
    private static string[] AppLanguageCodes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "Services/Localization/LanguageCatalog.cs"));
        return Regex.Matches(source, "new AppLanguage\\(\"([a-z]{2})\"").Select(m => m.Groups[1].Value).ToArray();
    }

    [Fact]
    public void EveryLanguageTheAppOffers_HasTheConsentTexts()
    {
        var offered = AppLanguageCodes();
        Assert.Equal(9, offered.Length);
        Assert.Equal(offered.OrderBy(c => c), ConsentTexts.Codes.OrderBy(c => c));
    }

    [Theory]
    [InlineData("ka")] [InlineData("en")] [InlineData("tr")] [InlineData("de")] [InlineData("pl")]
    [InlineData("es")] [InlineData("pt")] [InlineData("fr")] [InlineData("it")]
    public void EachLanguage_IsComplete(string code)
    {
        var t = ConsentTexts.For(code);
        foreach (var field in new[] { t.Title, t.Body, t.Agree, t.NotNow, t.SettingsTitle, t.StatusNone })
            Assert.False(string.IsNullOrWhiteSpace(field), code);

        Assert.True(t.Body.Length > 400, code + ": the body must explain what is collected");
        Assert.Equal(4, t.Body.Split("\n\n").Length);                         // purpose, what is sent, protections/use, withdrawal
        Assert.NotEqual(t.Agree, t.NotNow);
        foreach (var status in new[] { t.StatusGranted, t.StatusDeclined, t.StatusRevoked })
            Assert.Contains("{0}", status);

        // every format string is valid
        Assert.NotNull(string.Format(CultureInfo.InvariantCulture, t.StatusGranted, "10.10.2026 12:00"));
        Assert.NotNull(string.Format(CultureInfo.InvariantCulture, t.StatusDeclined, "11.10.2026 12:00"));
        Assert.NotNull(string.Format(CultureInfo.InvariantCulture, t.StatusRevoked, "12.10.2026 12:00"));
    }

    [Fact]
    public void OtherLanguages_AreTranslated_NotEnglishCopies()
    {
        var english = ConsentTexts.For("en");
        foreach (var code in ConsentTexts.Codes.Where(c => c != "en"))
        {
            var t = ConsentTexts.For(code);
            Assert.NotEqual(english.Body, t.Body);
            Assert.NotEqual(english.Title, t.Title);
            Assert.NotEqual(english.Agree, t.Agree);
            Assert.NotEqual(english.NotNow, t.NotNow);
        }
    }

    [Fact]
    public void Georgian_UsesTheRequestedTitleAndButtons()
    {
        var ka = ConsentTexts.For("ka");
        Assert.Equal("გავაუმჯობესოთ GCar360 ერთად! 🚗", ka.Title);
        Assert.Equal("თანახმა ვარ", ka.Agree);
        Assert.Equal("ახლა არა", ka.NotNow);
        Assert.StartsWith("GCar360-ს სურს ავტომობილის დიაგნოსტიკის ტექნიკური მონაცემების შეგროვება", ka.Body);
    }

    [Fact]
    public void AnUnknownLanguage_FallsBackToEnglish()
    {
        Assert.Equal(ConsentTexts.For("en"), ConsentTexts.For("xx"));
        Assert.Equal(ConsentTexts.For("en"), ConsentTexts.For(null));
    }

    [Fact]
    public void TheBody_DescribesWhatTheNeverClaimsOnlyWhatThePipelineEnforces()
    {
        // The claims below are the ones CollectionSanitizer / DataCollectionService actually enforce.
        var english = ConsentTexts.For("en").Body;
        Assert.Contains("read-only", english);
        Assert.Contains("VIN", english);
        Assert.Contains("pseudonymous, not fully anonymous", english);     // a device code is sent: do not claim anonymity
        Assert.Contains("technical value a module reported", english);     // module replies can contain live values
        Assert.Contains("never changes support claims", english);
        Assert.Contains("ask the server to delete", english);              // a request, not a promise that it succeeded
        // "anonymous" may only appear as "not fully anonymous"
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(english, "anonymous").Count,
                     System.Text.RegularExpressions.Regex.Matches(english, "not fully anonymous").Count);
        Assert.DoesNotContain("guarantee", english, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSettingsStatus_ReflectsEachState_InEveryLanguage()
    {
        foreach (var code in ConsentTexts.Codes)
        {
            var t = ConsentTexts.For(code);
            var storage = new MemoryConsentStorage();
            var clock = new TestClock();
            var policy = new ConsentPolicy(storage, clock.Read);

            Assert.Equal(t.StatusNone, ConsentTexts.Status(t, policy));

            policy.Decline();
            var declined = ConsentTexts.Status(t, policy);
            Assert.Contains("11.10.2026", declined);                       // the date it may be asked again

            clock.Advance(TimeSpan.FromHours(25));
            policy.Grant();
            Assert.Contains("11.10.2026", ConsentTexts.Status(t, policy));  // granted on the 11th (local time may shift the hour, not the day here)

            policy.Revoke();
            Assert.Contains("2026", ConsentTexts.Status(t, policy));
        }
    }

    [Fact]
    public void ConsentGivenBeforeDatesExisted_ShowsNoEmptyBrackets()
    {
        var storage = new MemoryConsentStorage();
        storage.SetBool(ConsentPolicy.GrantedKey, true);
        foreach (var code in ConsentTexts.Codes)
        {
            var line = ConsentTexts.Status(ConsentTexts.For(code), new ConsentPolicy(storage));
            Assert.DoesNotContain("()", line);
        }
    }
}
