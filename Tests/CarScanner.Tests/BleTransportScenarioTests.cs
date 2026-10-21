using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Tests.Simulation;

namespace CarScanner.Tests;

/// <summary>
/// The REAL <c>BluetoothService</c> BLE code (the 20-byte write chunks, the notification buffer that ends at the "&gt;"
/// prompt, the resync after a failed command, the reaction to a lost link) against a fake GATT characteristic that delivers
/// the simulated adapter's replies as notifications of a chosen size. SIMULATION ONLY: the fake proves what the app's code does
/// with such notifications, not how any real adapter or phone Bluetooth stack behaves. The Android Bluetooth Classic path
/// (a socket read loop inside #if ANDROID) is not compiled here and is NOT covered.
/// </summary>
[Collection("DiagnosticCapture")]    // one test switches the process-wide raw capture on
public sealed class BleTransportScenarioTests
{
    private static SimulatedElm327 Car()
    {
        var sim = new SimulatedElm327();
        sim.ObdAnswers["010D"] = "41 0D 3C";
        return sim;
    }

    // ------------------------------------------------------------------ assembling a reply from notifications

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(20)]
    public async Task AReplyDeliveredInNotificationsOfAnySize_IsAssembledIntoExactlyTheWholeReply(int size)
    {
        var ble = await BleHarness.CreateAsync(Car(), gatt => gatt.FragmentSize = size);

        var reply = await ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(3));

        Assert.Equal("41 00 BE 3F A8 13\r\r>", reply);               // nothing lost, nothing added, same bytes
        Assert.True(size >= 20 || ble.Gatt.Notifications > 1, "the reply was not actually split");   // 20 bytes is the whole reply in one
        Assert.False(ble.PendingResponseExists);
        Assert.Equal(0, ble.BufferedCharacters);                      // nothing left behind for the next command
    }

    [Fact]
    public async Task ThePromptArrivingAloneAfterAPause_EndsTheReplyThere_NotEarlier()
    {
        var ble = await BleHarness.CreateAsync(Car(), gatt =>
        {
            gatt.FragmentSize = "41 00 BE 3F A8 13\r\r".Length;      // the data and its carriage returns, then the prompt on its own
            gatt.FragmentDelayMilliseconds = 250;                    // wide margins: Windows timers can fire ~15 ms early under load
        });
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var reply = await ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(3));

        Assert.True(clock.ElapsedMilliseconds >= 150, "returned before the prompt arrived");
        Assert.Equal("41 00 BE 3F A8 13\r\r>", reply);
    }

    [Fact]
    public async Task ASlowReply_WithinTheTimeout_IsWaitedFor_AndOneBeyondItIsATimeout()
    {
        var ble = await BleHarness.CreateAsync(Car(), gatt =>
        {
            gatt.FragmentSize = 4;
            gatt.FragmentDelayMilliseconds = 40;                     // about 20 notifications over ~800 ms
        });

        var slowButInTime = await ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(3));
        Assert.Equal("41 00 BE 3F A8 13\r\r>", slowButInTime);

        await Assert.ThrowsAsync<TimeoutException>(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromMilliseconds(100)));
        Assert.True(ble.Service.IsConnected);                         // a slow reply is not a lost adapter
        await Task.Delay(1200);                                       // the rest of the late reply arrives and is not taken for anything
        var next = await ble.Service.SendCommandAsync("010C", TimeSpan.FromSeconds(3));
        Assert.Equal("41 0C 1A F8\r\r>", next);
    }

    [Fact]
    public async Task Characterization_BytesAfterThePromptInTheSameNotification_ReachTheCallerWithTheReply()
    {
        // A notification is taken as a unit: if the prompt is in it, everything in it belongs to the reply.
        var ble = await BleHarness.CreateAsync(Car(), gatt => gatt.Terminator = "\r\r>41 0C");

        var reply = await ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(3));

        Assert.Equal("41 00 BE 3F A8 13\r\r>41 0C", reply);
        Assert.Equal(0, ble.BufferedCharacters);                      // ...and nothing is left in the buffer for the next command
    }

    // ------------------------------------------------------------------ stale and late bytes

    [Fact]
    public async Task APartialReplyThatNeverEnds_TimesOut_AndNoneOfItReachesTheNextCommand()
    {
        var scripted = new ScriptedAdapter(Car());
        scripted.Fail("0100", () => new TimeoutException("simulated: the rest never comes"));
        var ble = await BleHarness.CreateAsync(scripted);
        ble.Gatt.BeforeAnswer = async line =>
        {
            if (line == "0100")
                await ble.Gatt.DeliverAsync("41 00 BE");              // the first half of the reply, without a prompt
        };

        await Assert.ThrowsAsync<TimeoutException>(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromMilliseconds(250)));
        var next = await ble.Service.SendCommandAsync("010C", TimeSpan.FromSeconds(3));

        Assert.Equal("41 0C 1A F8\r\r>", next);                       // exactly its own reply: the "41 00 BE" was discarded by the resync
        Assert.True(ble.Service.IsConnected);
        Assert.Contains("", ble.Gatt.Commands);                       // the bare carriage return (resync) was sent after the failure
    }

    [Fact]
    public async Task Characterization_ALateRemainderThatArrivesWhileTheNextCommandIsWaiting_IsTakenForItsReply()
    {
        // The transport cannot tell whose bytes a notification carries. A late remainder of the previous reply ("3F A8 13" and
        // the prompt) that lands while the next command is outstanding completes that command with the wrong text; the real
        // reply, arriving afterwards, finds nobody waiting. The protocol layer rejects a complete reply to ANOTHER PID, but not
        // a bare fragment like this one: see the next test.
        var ble = await BleHarness.CreateAsync(Car());
        ble.Gatt.BeforeAnswer = async line =>
        {
            if (line == "010C")
            {
                ble.Gatt.Notify("3F A8 13\r\r>");                     // a late remainder, right after the write
                await Task.Delay(100);                                // ...then the real reply
            }
        };

        var reply = await ble.Service.SendCommandAsync("010C", TimeSpan.FromSeconds(3));
        await Task.Delay(300);                                        // the real reply arrives and is dropped

        Assert.Equal("3F A8 13\r\r>", reply);
        Assert.Equal(0, ble.BufferedCharacters);
        // The next command is not affected: it gets its own reply.
        ble.Gatt.BeforeAnswer = null;
        Assert.Equal("41 0D 3C\r\r>", await ble.Service.SendCommandAsync("010D", TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Characterization_AtTheProtocolLayer_ALateRemainderIsReturnedAsTheAnswer_ButACompleteReplyToAnotherPidIsRetried()
    {
        var sim = Car();
        var ble = await BleHarness.CreateAsync(sim);
        var rig = await AdapterRigs.CreateAsync(ble.Service, connect: false);

        // (1) a complete reply to the PREVIOUS request arrives first: rejected, the retry gets the real answer.
        var calls = 0;
        ble.Gatt.BeforeAnswer = async line =>
        {
            if (line == "010D" && Interlocked.Increment(ref calls) == 1)
            {
                ble.Gatt.Notify("41 0C 1A F8\r\r>");
                await Task.Delay(120);
            }
        };
        var corrected = await rig.Elm.SendCommandAsync("010D", TimeSpan.FromSeconds(2), 3, true);
        Assert.Equal("41 0D 3C", corrected);

        // (2) only a bare fragment arrives first: nothing in the protocol layer recognises it as foreign.
        ble.Gatt.BeforeAnswer = async line =>
        {
            if (line == "010C")
            {
                ble.Gatt.Notify("3F A8 13\r\r>");
                await Task.Delay(120);
            }
        };
        var fragment = await rig.Elm.SendCommandAsync("010C", TimeSpan.FromSeconds(2), 3, true);
        Assert.Equal("3F A8 13", fragment);
        Assert.Empty(ObdResponseParser.ExtractPayload(fragment, 0x41, 0x0C));      // the parser still refuses it: there is no 41 0C in it
    }

    // ------------------------------------------------------------------ a request that is outstanding when something happens

    [Fact]
    public async Task ALostLink_WhileARequestIsOutstanding_FailsThatRequestAtOnce_WithTheIOExceptionTheCodeSets()
    {
        // Stage 2A fix. Before it, MarkBleDisconnected failed the pending response with an IOException and then set the shared
        // field to null; SendCommandAsync, once woken, read that field again and dereferenced null, so the request ended with a
        // NullReferenceException (observed every time on this machine). Each request now keeps its own pending response, so the
        // caller sees the IOException that says the link was lost.
        var scripted = new ScriptedAdapter(Car());
        scripted.Fail("0100", () => new TimeoutException("never answered"));
        var ble = await BleHarness.CreateAsync(scripted);
        ble.Gatt.BeforeAnswer = line =>
        {
            if (line == "0100")
                ble.LoseTheLink();                                    // the BLE stack reports the device gone, while 0100 is waiting
            return Task.CompletedTask;
        };
        using var log = new LogScope();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var failure = await Record.ExceptionAsync(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(30)));

        Assert.NotNull(failure);
        Assert.True(clock.ElapsedMilliseconds < 5000, "the request waited for its timeout instead of failing when the link was lost");
        Assert.False(ble.Service.IsConnected);
        Assert.Equal(new[] { false }, ble.ConnectionEvents);
        Assert.NotNull(log.Find("BLE link event: OBD Bluetooth კავშირი გაითიშა. (was connected: True)"));
        var lost = Assert.IsType<IOException>(failure);               // never a NullReferenceException
        Assert.Equal("OBD Bluetooth კავშირი გაითიშა.", lost.Message); // the reason the BLE stack reported
        Assert.Equal(AdapterFailureKind.AdapterLinkLost, AdapterReadFailure.Classify(failure!).Kind);
        Assert.Equal(ExchangeLayer.Transport, AdapterExchangeClassifier.Classify(failure!).Layer);
        Assert.False(ble.PendingResponseExists);                      // nothing is left waiting
        // A request on the lost link is refused clearly.
        await Assert.ThrowsAsync<InvalidOperationException>(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(1)));
    }

    public static IEnumerable<object[]> TwentyFiveRounds() => Enumerable.Range(1, 25).Select(round => new object[] { round });

    [Theory]
    [MemberData(nameof(TwentyFiveRounds))]
    public async Task ALostLink_BeforeAnyByteOfTheReplyArrived_IsAlwaysAnIOException_NeverANullReference(int round)
    {
        // Stage 2A: the same loss, repeated, to show the outcome does not depend on thread timing.
        var scripted = new ScriptedAdapter(Car());
        scripted.Fail("0100", () => new TimeoutException("never answered"));
        var ble = await BleHarness.CreateAsync(scripted);
        ble.Gatt.BeforeAnswer = line =>
        {
            if (line == "0100")
                ble.LoseTheLink();
            return Task.CompletedTask;
        };

        var failure = await Record.ExceptionAsync(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(30)));

        Assert.True(failure is IOException, $"round {round}: {failure?.GetType().Name ?? "no exception"}");
        Assert.False(ble.PendingResponseExists);
        Assert.False(ble.Service.IsConnected);
    }

    [Fact]
    public async Task ALostLink_AfterPartOfTheReplyArrived_FailsTheRequest_AndNoneOfThatPartReachesTheNextRequest()
    {
        var sim = Car();
        var scripted = new ScriptedAdapter(sim);
        scripted.Fail("0100", () => new TimeoutException("the rest never comes"));
        var ble = await BleHarness.CreateAsync(scripted);
        var firstSession = ble.Gatt;
        firstSession.BeforeAnswer = async line =>
        {
            if (line == "0100")
            {
                await firstSession.DeliverAsync("41 00 BE");          // the first half of the reply, no prompt yet
                ble.LoseTheLink();
            }
        };

        var failure = await Record.ExceptionAsync(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(30)));

        Assert.IsType<IOException>(failure);
        Assert.Equal(0, ble.BufferedCharacters);                      // the loss cleared what had arrived

        await ble.ReconnectAsync(scripted);
        Assert.Equal("41 0C 1A F8\r\r>", await ble.Service.SendCommandAsync("010C", TimeSpan.FromSeconds(3)));   // exactly its own reply
        Assert.Equal(0, ble.BufferedCharacters);
    }

    [Fact]
    public async Task ALateReplyOfTheOldSession_IsNotTakenForTheReplyOfARequestOnTheNewSession()
    {
        var sim = Car();
        var ble = await BleHarness.CreateAsync(sim);
        var oldSession = ble.Gatt;
        ble.LoseTheLink();                                            // an idle link is lost

        var newSession = await ble.ReconnectAsync(sim);
        newSession.BeforeAnswer = async line =>
        {
            if (line == "010D")
            {
                oldSession.Notify("41 0C 1A F8\r\r>");                // a late reply of the old session, while 010D is waiting
                await Task.Delay(100);
            }
        };

        var reply = await ble.Service.SendCommandAsync("010D", TimeSpan.FromSeconds(3));

        Assert.Equal("41 0D 3C\r\r>", reply);                         // the old session's bytes are not delivered to anything
        Assert.Equal(0, ble.BufferedCharacters);
    }

    [Fact]
    public async Task ALostLink_WhileTheCleanupAfterATimeoutRuns_KeepsTheTimeoutAsTheError_AndLeavesNothingBehind()
    {
        var scripted = new ScriptedAdapter(Car());
        scripted.Fail("0100", () => new TimeoutException("never answered"));
        var ble = await BleHarness.CreateAsync(scripted);
        ble.Gatt.BeforeAnswer = line =>
        {
            if (line.Length == 0)                                     // the bare carriage return that resynchronises the adapter
                ble.LoseTheLink();
            return Task.CompletedTask;
        };

        var failure = await Record.ExceptionAsync(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromMilliseconds(250)));

        Assert.IsType<TimeoutException>(failure);                     // a genuine timeout stays a timeout
        Assert.False(ble.Service.IsConnected);
        Assert.False(ble.PendingResponseExists);
        Assert.Equal(new[] { false }, ble.ConnectionEvents);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ALostLink_WhileTheCleanupAfterACancellationRuns_KeepsTheCancellationAsTheError()
    {
        var scripted = new ScriptedAdapter(Car());
        scripted.Fail("0100", () => new TimeoutException("never answered"));
        var ble = await BleHarness.CreateAsync(scripted);
        ble.Gatt.BeforeAnswer = line =>
        {
            if (line.Length == 0)
                ble.LoseTheLink();
            return Task.CompletedTask;
        };
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(120));

        var failure = await Record.ExceptionAsync(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(10), cancel.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.False(ble.Service.IsConnected);
        Assert.False(ble.PendingResponseExists);
    }

    [Fact]
    public async Task ALostLink_DuringAMonitorCommand_FailsWithTheIOException_AndTheStopCarriageReturnIsNotSent()
    {
        var scripted = new ScriptedAdapter(Car());
        scripted.Fail("ATMA", () => new TimeoutException("monitoring"));
        var ble = await BleHarness.CreateAsync(scripted);
        ble.Gatt.BeforeAnswer = line =>
        {
            if (line == "ATMA")
                ble.LoseTheLink();
            return Task.CompletedTask;
        };

        var failure = await Record.ExceptionAsync(() => ble.Service.SendCommandAsync(ObdMonitorCommand.Create("ATMA", 400), TimeSpan.FromSeconds(10)));

        Assert.IsType<IOException>(failure);
        Assert.Equal(new[] { "ATMA" }, ble.Gatt.Commands);            // nothing was written to a link that is gone
        Assert.False(ble.PendingResponseExists);
    }

    [Fact]
    public async Task ARequestThatIsOutstandingWhenTheLinkIsLost_CannotCompleteTheNextRequest_OnceTheLinkIsBack()
    {
        // The first request is failed by the loss; the request after the reconnect gets its own reply from the new session only.
        var sim = Car();
        var scripted = new ScriptedAdapter(sim);
        scripted.Fail("0100", () => new TimeoutException("never answered"));
        var ble = await BleHarness.CreateAsync(scripted);
        ble.Gatt.BeforeAnswer = line =>
        {
            if (line == "0100")
                ble.LoseTheLink();
            return Task.CompletedTask;
        };
        var first = ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(30));
        await Assert.ThrowsAsync<IOException>(() => first);

        await ble.ReconnectAsync(scripted);
        var second = await ble.Service.SendCommandAsync("010C", TimeSpan.FromSeconds(3));

        Assert.Equal("41 0C 1A F8\r\r>", second);
        Assert.Equal(new[] { "010C" }, ble.Gatt.Commands);            // the new session saw only the new request
    }

    [Fact]
    public void TheRequestCode_NeverReadsThePendingResponseFieldAgain_AfterItHasCreatedItsOwn()
    {
        // Source pin for the Stage 2A fix (the service cannot be run against a real stack here): SendCommandAsync waits on its
        // own local pending response, so the disconnect handler's clearing of the shared field cannot reach it.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "Services", "Bluetooth", "BluetoothService.cs")).Replace("\r\n", "\n");

        var start = source.IndexOf("public async Task<string> SendCommandAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("// BLE CHARACTERISTICS", start, StringComparison.Ordinal);
        var body = source[start..end];

        Assert.Contains("_pendingResponse = pending;", body);
        Assert.DoesNotContain("_pendingResponse.Task", body);
        Assert.Contains("pending.Task", body);
        // The disconnect handler takes the field out in one step.
        var handler = source[source.IndexOf("private void MarkBleDisconnected(", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("ADAPTER CHECK + AUTOMATIC RECONNECT", StringComparison.Ordinal)];
        Assert.Contains("Interlocked.Exchange(", handler);
        Assert.DoesNotContain("_pendingResponse = null;", handler);
    }

    [Fact]
    public async Task Characterization_AtTheProtocolLayer_ALostLinkDuringACommand_SurfacesAsATimeoutAfterTheRetries()
    {
        // The error of the lost link (see the test above) is swallowed by the retry loop, which retries any non-cancellation
        // error; the retries then find the link down (InvalidOperationException), and the loop ends with Elm327TimeoutException
        // "failed after N attempt(s)". The failure kind therefore reads NoReplyInTime, not AdapterLinkLost. That conversion is the
        // protocol layer's retry policy, which Stage 2A does not change; the connection log (the "BLE link event" line) and the
        // raw capture (one entry per attempt, with the exception type) tell a lost link from a silent adapter.
        // The raw capture is process-wide: while it is on, tests in other collections that run the protocol layer write into
        // it too. A command that no other test sends keeps this test's count exact.
        const string OwnCommand = "01FA";
        var sim = Car();
        var scripted = new ScriptedAdapter(sim);
        var ble = await BleHarness.CreateAsync(scripted);
        var rig = await AdapterRigs.CreateAsync(ble.Service, connect: false);
        scripted.Fail(OwnCommand, () => new TimeoutException("never answered"));
        ble.Gatt.BeforeAnswer = line =>
        {
            if (line == OwnCommand)
                ble.LoseTheLink();
            return Task.CompletedTask;
        };
        using var log = new LogScope();
        AdapterDiagnosticCapture.Enable(TimeSpan.FromMinutes(1));
        try
        {
            var failure = await Record.ExceptionAsync(() => rig.Elm.SendCommandAsync(OwnCommand, TimeSpan.FromSeconds(5), 3, true));

            Assert.IsType<Elm327TimeoutException>(failure);
            Assert.Contains("failed after 3 attempt(s)", failure!.Message);
            Assert.Equal(AdapterFailureKind.NoReplyInTime, AdapterReadFailure.Classify(failure).Kind);
            Assert.NotNull(log.Find("BLE link event:"));                // the log still shows the link was lost
            var capture = AdapterDiagnosticCapture.Export().Split('\n').Where(line => line.Contains($" {OwnCommand} try ")).ToList();
            Assert.Equal(3, capture.Count);
            // The first attempt: the lost link, as the IOException the service sets (it used to be a NullReferenceException).
            Assert.Contains(capture, line => line.Contains("try 1/3") && line.Contains("[transport]") && line.Contains("(IOException)"));
            Assert.DoesNotContain(capture, line => line.Contains("NullReferenceException"));
            // The retries: the transport is down. Labelled a transport fault, not a reply that failed to parse.
            Assert.Contains(capture, line => line.Contains("try 2/3") && line.Contains("Fault [transport]") && line.Contains("(InvalidOperationException)"));
            Assert.Contains(capture, line => line.Contains("try 3/3") && line.Contains("Fault [transport]"));
        }
        finally
        {
            AdapterDiagnosticCapture.Disable();
            AdapterDiagnosticCapture.Clear();
        }
    }

    [Fact]
    public async Task ACommandCancelledByTheCaller_IsACancellation_AndTheLinkAndTheNextCommandAreFine()
    {
        var scripted = new ScriptedAdapter(Car());
        scripted.Fail("0100", () => new TimeoutException("never answered"));
        var ble = await BleHarness.CreateAsync(scripted);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(120));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(10), cancel.Token));

        Assert.True(ble.Service.IsConnected);                         // a cancellation never ends the link
        Assert.Contains("", ble.Gatt.Commands);                        // the adapter was resynchronised with a bare carriage return
        Assert.Equal("41 0C 1A F8\r\r>", await ble.Service.SendCommandAsync("010C", TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task TwoRequestsAtTheSameTime_AreSentOneAfterTheOther_AndEachGetsItsOwnReply()
    {
        var ble = await BleHarness.CreateAsync(Car(), gatt =>
        {
            gatt.FragmentSize = 3;
            gatt.FragmentDelayMilliseconds = 5;
        });

        var first = ble.Service.SendCommandAsync("010C", TimeSpan.FromSeconds(5));
        var second = ble.Service.SendCommandAsync("010D", TimeSpan.FromSeconds(5));
        var third = ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(5));
        var replies = await Task.WhenAll(first, second, third);

        Assert.Equal("41 0C 1A F8\r\r>", replies[0]);
        Assert.Equal("41 0D 3C\r\r>", replies[1]);
        Assert.Equal("41 00 BE 3F A8 13\r\r>", replies[2]);
        lock (ble.Gatt.Commands)
            Assert.Equal(3, ble.Gatt.Commands.Count);                 // each was written once, and none while another was waiting
    }

    [Fact]
    public async Task AnAdapterThatStopsAnsweringAtAll_IsTreatedAsLostAfterThreeFailedChecks_AndNotBefore()
    {
        var ble = await BleHarness.CreateAsync(Car());
        ble.Gatt.Silent = true;                                       // not even the prompt after a bare carriage return
        using var log = new LogScope();

        await Assert.ThrowsAsync<TimeoutException>(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromMilliseconds(150)));
        Assert.True(ble.Service.IsConnected);
        await Assert.ThrowsAsync<TimeoutException>(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromMilliseconds(150)));
        Assert.True(ble.Service.IsConnected);                         // two silent checks: still the same link
        await Assert.ThrowsAsync<TimeoutException>(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromMilliseconds(150)));

        Assert.False(ble.Service.IsConnected);                        // the third, followed by a failed ATI check, ends it
        Assert.Equal(new[] { false }, ble.ConnectionEvents);
        Assert.NotNull(log.Find("Adapter stopped answering 3 resync checks in a row; treating the link as lost"));
        lock (ble.Gatt.Commands)
            Assert.Contains("ATI", ble.Gatt.Commands);                // the final check before giving up
    }

    // ------------------------------------------------------------------ what goes over the air

    [Theory]
    [InlineData("0100", new[] { 5 })]                                  // the command and its carriage return: one write
    [InlineData("ATSH7E1", new[] { 8 })]
    [InlineData("0123456789012345678", new[] { 20 })]                  // 19 characters + CR = exactly one full chunk
    [InlineData("01234567890123456789", new[] { 20, 1 })]              // 20 characters + CR: a full chunk, then the carriage return alone
    [InlineData("0123456789012345678901234567890123456789012", new[] { 20, 20, 4 })]
    public async Task CommandsAreWrittenInChunksOfAtMostTwentyBytes_AsTheyAlwaysWere(string command, int[] expected)
    {
        var ble = await BleHarness.CreateAsync(Car());

        await ble.Service.SendCommandAsync(command, TimeSpan.FromSeconds(3));

        lock (ble.Gatt.Writes)
            Assert.Equal(expected, ble.Gatt.Writes.Select(chunk => chunk.Length));
        Assert.Equal(command, ble.Gatt.Commands.Single());             // and the chunks add up to the command
    }

    [Fact]
    public void SourcePin_TheConnectionLog_NamesTheTransportAndTheGattProfile_ForBleAndForClassic()
    {
        // ConnectAsync needs the real Bluetooth stack (and the classic socket exists only on Android), so this is read from the
        // source, not run. It pins what the log is meant to say when the app connects.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "Services", "Bluetooth", "BluetoothService.cs")).Replace("\r\n", "\n");

        // One place sets the label for both kinds, right after a successful connect, before initialization starts.
        Assert.Contains("AdapterDiagnosticCapture.TransportLabel = item.IsClassicBluetooth ? \"Classic SPP\" : \"BLE\";", source);
        Assert.Contains("LinkEventLog.Add($\"Connected over {CarScanner.Services.OBD.AdapterDiagnosticCapture.TransportLabel}\");", source);
        // The GATT profile that was chosen (a known UART service, or the generic fallback) is written down before it is used.
        Assert.Contains("BLE profile: known UART service {profile.Service}, write {profile.Write}, notify {profile.Notify}", source);
        Assert.Contains("BLE profile: no known UART service matched; using the first writable", source);
        // The existing connect and failure lines are still there.
        Assert.Contains("LinkEventLog.Add($\"Connecting to {item.DisplayName} ({(item.IsClassicBluetooth ? \"classic\" : \"BLE\")})\");", source);
        Assert.Contains("LinkEventLog.Add($\"Connect failed: {connectError.GetType().Name}\");", source);
    }

    [Fact]
    public async Task ARequestWithNoCommand_OrOnALinkThatIsDown_IsRefusedWithoutWritingAnything()
    {
        var ble = await BleHarness.CreateAsync(Car());

        await Assert.ThrowsAsync<ArgumentException>(() => ble.Service.SendCommandAsync("  ", TimeSpan.FromSeconds(1)));
        ble.LoseTheLink();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ble.Service.SendCommandAsync("0100", TimeSpan.FromSeconds(1)));

        lock (ble.Gatt.Writes)
            Assert.Empty(ble.Gatt.Writes);
    }
}
