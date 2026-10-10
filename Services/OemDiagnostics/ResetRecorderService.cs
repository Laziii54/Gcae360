using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using CarScanner.Models;
using CarScanner.Services.OBD;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Reset recorder ("ჩაწერის რეჟიმი"). The adapter only LISTENS — nothing is
/// sent to the car. Two ways to learn a car's reset command:
///
///  • MenuReset — the driver resets oil/service from the car's own menu.
///    First a few quiet seconds are recorded as a baseline, then everything
///    while the driver resets; frames that appear only during the reset are
///    the candidates. Works on cars whose OBD socket carries the internal
///    bus (many GM, Ford, Hyundai/Kia). Cars with a gateway (most VW/Audi,
///    BMW, Mercedes) do not show these messages on the socket.
///
///  • ToolSession — a workshop scan tool performs the reset while the phone's
///    adapter is plugged in through an OBD splitter (Y-cable). Only diagnostic
///    traffic (0x700–0x7FF or 0x18DAxxxx) is recorded, so the exact request and
///    the car's answer can be read. This also works on gateway cars.
///
/// The recording is analysed on the phone and sent to the GCar360 server,
/// where a command is reviewed before it is ever used to reset a car.
/// </summary>
public sealed class ResetRecorderService(OBDService obd, AccountService account)
{
    public enum Mode { MenuReset, ToolSession }

    public sealed record Frame(int Ms, int Id, string Data, bool Baseline);

    public sealed record Candidate(string Kind, int Id, string Data, string Note);

    public sealed record Recording(
        Mode RecordingMode,
        bool Extended29Bit,
        IReadOnlyList<Frame> Frames,
        IReadOnlyList<Candidate> Candidates,
        bool BufferOverflow);

    public sealed record Status(string Phase, int Frames, int Seconds);

    private const int BaselineSeconds = 6;
    private const int MaxSeconds = 150;
    private const int MaxFrames = 40000;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Records until <paramref name="stop"/> is cancelled (the user taps "Done"),
    /// or at most <see cref="MaxSeconds"/>. Phases: "baseline" (MenuReset only:
    /// do not touch anything) → "record".
    /// </summary>
    public async Task<Recording> RecordAsync(
        Mode mode,
        CancellationToken stop,
        IProgress<Status>? progress,
        CancellationToken token)
    {
        if (!obd.IsInitialized)
            await obd.InitializeAsync(token).ConfigureAwait(false);
        if (!obd.IsCanProtocol)
            throw new NotSupportedException("The recorder needs a CAN car (2008+).");

        var ext29 = obd.CurrentProtocol is ObdProtocol.ISO_15765_4_CAN_29BIT_500K or ObdProtocol.ISO_15765_4_CAN_29BIT_250K;
        int? filter = null, mask = null;
        if (mode == Mode.ToolSession)
        {
            filter = ext29 ? 0x18DA0000 : 0x700;
            mask = ext29 ? 0x1FFF0000 : 0x700;
        }

        var frames = new List<Frame>();
        var overflow = false;
        var clock = Stopwatch.StartNew();

        await obd.RunDtcModuleSessionAsync(async session =>
        {
            while (!token.IsCancellationRequested)
            {
                var baseline = mode == Mode.MenuReset && clock.Elapsed < TimeSpan.FromSeconds(BaselineSeconds);
                if (!baseline && stop.IsCancellationRequested)
                    break;
                if (clock.Elapsed > TimeSpan.FromSeconds(MaxSeconds) || frames.Count >= MaxFrames)
                    break;

                progress?.Report(new Status(baseline ? "baseline" : "record", frames.Count, (int)clock.Elapsed.TotalSeconds));

                var startMs = (int)clock.ElapsedMilliseconds;
                var raw = await session.MonitorRawAsync(filter, mask, ext29, baseline ? 1500 : 2000, token).ConfigureAwait(false);
                if (raw.Contains("BUFFER FULL", StringComparison.OrdinalIgnoreCase))
                    overflow = true;

                foreach (var (id, data) in Parse(raw, ext29))
                    frames.Add(new Frame(startMs, id, data, baseline));
            }
        }, token).ConfigureAwait(false);

        progress?.Report(new Status("done", frames.Count, (int)clock.Elapsed.TotalSeconds));
        var candidates = mode == Mode.ToolSession ? AnalyseToolSession(frames) : AnalyseMenuReset(frames);
        return new Recording(mode, ext29, frames, candidates, overflow);
    }

    /// <summary>"7E8064100BE3FA813" → (0x7E8, "064100BE3FA813"). Status lines are skipped.</summary>
    public static IEnumerable<(int Id, string Data)> Parse(string raw, bool ext29) =>
        CarScanner.Services.Passive.CanFrameParser.Parse(raw, ext29);

    /// <summary>Frames seen only while the driver reset (not in the quiet baseline).</summary>
    public static IReadOnlyList<Candidate> AnalyseMenuReset(IReadOnlyList<Frame> frames)
    {
        var baselineIds = frames.Where(f => f.Baseline).Select(f => f.Id).ToHashSet();
        var baselinePairs = frames.Where(f => f.Baseline).Select(f => (f.Id, f.Data)).ToHashSet();
        var action = frames.Where(f => !f.Baseline).ToArray();

        var newIds = action
            .Where(f => !baselineIds.Contains(f.Id))
            .GroupBy(f => (f.Id, f.Data))
            .Select(g => new Candidate("new-id", g.Key.Id, g.Key.Data, $"{g.Count()}× · first at {g.Min(f => f.Ms) / 1000.0:0.0}s"));

        // Known IDs with a payload never seen before, and only a few times (a command, not a stream).
        // Messages publicly known to carry an oil-reset request are kept even when repeated.
        var changed = action
            .Where(f => baselineIds.Contains(f.Id) && !baselinePairs.Contains((f.Id, f.Data)))
            .GroupBy(f => (f.Id, f.Data))
            .Where(g => g.Count() <= 5 || KnownResetMessages.ContainsKey(g.Key.Id))
            .Select(g => new Candidate(
                KnownResetMessages.TryGetValue(g.Key.Id, out var known) ? "known-reset-message" : "new-data",
                g.Key.Id,
                g.Key.Data,
                (known is null ? "" : known + " · ") + $"{g.Count()}× · first at {g.Min(f => f.Ms) / 1000.0:0.0}s"));

        return changed.Where(c => c.Kind == "known-reset-message")
            .Concat(newIds)
            .Concat(changed.Where(c => c.Kind != "known-reset-message"))
            .Take(60)
            .ToArray();
    }

    /// <summary>
    /// 11-bit messages that public CAN databases describe as carrying an
    /// oil-life reset request (to recognise them in a recording; never sent).
    /// </summary>
    private static readonly Dictionary<int, string> KnownResetMessages = new()
    {
        // Ford HS-CAN "Cluster_Info" (opendbc ford_lincoln_base_pt / cgea1_2, MIT): EngOilLife_B_RqReset.
        [0x430] = "Ford cluster: oil-life reset request bit (opendbc)",
        // GM E38/E67 era: engine message that carries the oil-life reset request.
        [0x1F1] = "GM: engine message with oil-life reset request",
    };

    /// <summary>Diagnostic requests a scan tool sent (session, security, write, routine, IO) and the answers.</summary>
    public static IReadOnlyList<Candidate> AnalyseToolSession(IReadOnlyList<Frame> frames)
    {
        var interesting = new HashSet<int> { 0x10, 0x11, 0x14, 0x27, 0x2E, 0x2F, 0x31, 0x3B, 0xAE, 0x28, 0x85, 0x3E };
        var result = new List<Candidate>();
        foreach (var frame in frames)
        {
            if (frame.Data.Length < 4)
                continue;
            var pci = Convert.ToInt32(frame.Data[..2], 16);
            var type = pci >> 4;
            var sidIndex = type == 0 ? 2 : type == 1 ? 4 : -1;
            if (sidIndex < 0 || frame.Data.Length < sidIndex + 2)
                continue;

            var sid = Convert.ToInt32(frame.Data.Substring(sidIndex, 2), 16);
            string? kind = null;
            if (interesting.Contains(sid))
                kind = type == 1 ? "request (multi-frame)" : "request";
            else if (sid >= 0x40 && interesting.Contains(sid - 0x40))
                kind = "positive answer";
            else if (sid == 0x7F && frame.Data.Length >= sidIndex + 6 &&
                     interesting.Contains(Convert.ToInt32(frame.Data.Substring(sidIndex + 2, 2), 16)))
                kind = "negative answer";

            if (kind is null || (sid is 0x3E or 0x7E)) // tester-present noise
                continue;

            var note = sid == 0x27 || sid == 0x67
                ? "security access (seed/key) — replay may not be possible"
                : $"at {frame.Ms / 1000.0:0.0}s";
            result.Add(new Candidate(kind, frame.Id, frame.Data, note));
        }

        return result
            .GroupBy(c => (c.Id, c.Data))
            .Select(g => g.First())
            .Take(80)
            .ToArray();
    }

    /// <summary>Sends the recording to the server for review (at most the first 6 000 frames).</summary>
    public async Task<bool> UploadAsync(VehicleInfo vehicle, Recording recording, string itemName, double? before, double? after, CancellationToken token)
    {
        try
        {
            using var request = await account.CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/reset-recordings");
            request.Content = JsonContent.Create(new
            {
                make = vehicle.Manufacturer,
                model = vehicle.Model,
                year = vehicle.Year,
                vinPrefix = vehicle.VIN is { Length: 17 } vin ? vin[..11] : null,
                mode = recording.RecordingMode.ToString(),
                item = itemName,
                extended29Bit = recording.Extended29Bit,
                bufferOverflow = recording.BufferOverflow,
                valueBefore = before,
                valueAfter = after,
                appVersion = AppInfo.Current.VersionString,
                candidates = recording.Candidates.Select(c => new { kind = c.Kind, id = c.Id.ToString(recording.Extended29Bit ? "X8" : "X3"), data = c.Data, note = c.Note }),
                frames = recording.Frames.Take(6000).Select(f => $"{f.Ms} {(f.Baseline ? "B" : "R")} {f.Id.ToString(recording.Extended29Bit ? "X8" : "X3")} {f.Data}")
            });
            using var response = await _http.SendAsync(request, token);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
