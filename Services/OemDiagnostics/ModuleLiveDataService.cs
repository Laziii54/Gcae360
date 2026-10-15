using System.Globalization;
using System.Text.Json;
using CarScanner.Models;
using CarScanner.Services.OBD;
using SQLite;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Live values from the car's modules (ABS wheel speeds, steering angle,
/// battery cells, transmission …) using the signal definitions of the offline
/// catalog (OBDb).
///
/// Safety rules:
/// • only READ requests are sent (UDS 22 / KWP 21);
/// • definitions are taken for the car's own make: exact model first, then
///   identifiers that at least two models of the same make share;
/// • a value is shown only after a positive, identifier-matching reply whose
///   decoded value is inside the definition's valid range.
/// </summary>
public sealed class ModuleLiveDataService
{
    private const int MaxExactRequests = 160;
    private const int MaxBrandRequests = 160;

    private readonly OBDService _obd;
    private readonly DtcCatalogService _catalog;

    private static Dictionary<string, string>? _namesKa;
    private static readonly SemaphoreSlim NamesGate = new(1, 1);

    public ModuleLiveDataService(OBDService obd, DtcCatalogService catalog)
    {
        _obd = obd;
        _catalog = catalog;
    }

    /// <summary>The car of the last module scan (set by the scan page).</summary>
    public VehicleInfo? LastVehicle { get; set; }

    public sealed record DiscoveryResult(
        IReadOnlyList<ModuleLiveGroup> Groups,
        string NoteKa,
        string NoteEn);

    // ------------------------------------------------------------------
    // Definitions
    // ------------------------------------------------------------------

    private sealed class SignalFormat
    {
        public double? Mul { get; set; }
        public double? Div { get; set; }
        public double? Add { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? NullMin { get; set; }
        public double? NullMax { get; set; }
        public bool Sign { get; set; }
        public Dictionary<string, string>? Map { get; set; }
    }

    private static readonly System.Text.RegularExpressions.Regex CylinderPattern = new(
        @"cyl(?:inder)?[\s_#.\-]*(\d{1,2})(?!\d)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Cylinder number referenced by a signal name/path, e.g. "Misfire cylinder 6" → 6.</summary>
    private static int? CylinderNumber(string? text)
    {
        var match = CylinderPattern.Match(text ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) && number is >= 1 and <= 16
            ? number
            : null;
    }

    /// <summary>Engine cylinder count from the decoded engine text ("1.4 L 4 cyl", "V6", "L4", "4-cylinder").</summary>
    private static int? EngineCylinderCount(string? engine)
    {
        var text = engine ?? string.Empty;
        foreach (var pattern in new[]
                 {
                     @"(\d{1,2})\s*-?\s*cyl",
                     @"\b[VWLIH](\d{1,2})\b",
                     @"(\d{1,2})\s*-?\s*(?:ცილინდრ)"
                 })
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var count) && count is >= 1 and <= 16)
                return count;
        }

        return null;
    }

    private sealed class SignalDef
    {
        public string Name { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public int BitOffset { get; init; }
        public int BitLength { get; init; }
        public SignalFormat Format { get; init; } = new();
        public bool Exact { get; init; }
        public ModuleLiveSignal? Signal { get; set; }
    }

    private sealed class RequestDef
    {
        public int Tx { get; init; }
        public int Rx { get; init; }
        public bool Ext29 { get; init; }
        public byte? Ext { get; init; }
        public byte Service { get; init; }
        public int Did { get; init; }
        public bool Exact { get; set; }
        public int Models { get; set; }
        public List<SignalDef> Signals { get; } = new();
        public int Failures { get; set; }
        public bool SlowChanging => Signals.Count > 0 && Signals.All(signal =>
            signal.Path.StartsWith("Battery.Cells", StringComparison.OrdinalIgnoreCase) ||
            signal.Path.StartsWith("Battery.Modules", StringComparison.OrdinalIgnoreCase));
        public int Priority => Signals.Count == 0 ? 99 : Signals.Min(signal => GroupOrder(signal.Path));
        public string Key => $"{Tx:X}|{Rx:X}|{Ext29}|{Ext}|{Service:X2}|{Did:X4}";
    }

    /// <summary>What the car did with the discovery requests (shown when nothing answered, to see why).</summary>
    private sealed class ReadStats
    {
        public int Sent { get; set; }
        public int Positive { get; set; }
        public int NoReply { get; set; }
        public int Negative { get; set; }
        public int Errors { get; set; }
        public int SkippedSilent { get; set; }
        public string? FirstError { get; set; }
        public SortedSet<string> NegativeCodes { get; } = new(StringComparer.Ordinal);

        public string Summary()
        {
            var text = $"sent {Sent}, positive {Positive}, no reply {NoReply}, negative {Negative}";
            if (NegativeCodes.Count > 0) text += $" (NRC {string.Join(",", NegativeCodes)})";
            if (SkippedSilent > 0) text += $", skipped {SkippedSilent} (module silent)";
            if (Errors > 0) text += $", errors {Errors}" + (FirstError is null ? string.Empty : $" [{FirstError}]");
            return text;
        }
    }

    private sealed class PidRow
    {
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? Header { get; set; }
        public string? CommandJson { get; set; }
        public string? Name { get; set; }
        public string? Path { get; set; }
        public string? Unit { get; set; }
        public int BitLength { get; set; }
        public int BitOffset { get; set; }
        public string? Eax { get; set; }
        public string? FormatJson { get; set; }
        public string? RawJson { get; set; }
    }

    // ------------------------------------------------------------------
    // Run: discover, then read continuously until cancelled
    // ------------------------------------------------------------------

    /// <param name="debugOnly">
    /// False (default, the normal "module live data" page): only catalog entries already reviewed (debug = 0).
    /// True: only the NOT-yet-reviewed research entries (debug = 1) — nothing this returns has been confirmed
    /// against a real car, so it must never be shown on the normal page or to a non-admin caller.
    /// </param>
    public async Task RunAsync(
        VehicleInfo vehicle,
        Func<DiscoveryResult, Task> onDiscovered,
        IProgress<double>? progress,
        CancellationToken token,
        bool debugOnly = false)
    {
        if (!_obd.IsInitialized)
        {
            await onDiscovered(new DiscoveryResult(Array.Empty<ModuleLiveGroup>(),
                "OBD ადაპტერი არ არის დაკავშირებული.",
                "The OBD adapter is not connected.")).ConfigureAwait(false);
            return;
        }

        var protocol = _obd.CurrentProtocol;
        if (protocol is not (ObdProtocol.ISO_15765_4_CAN_11BIT_500K
                or ObdProtocol.ISO_15765_4_CAN_11BIT_250K
                or ObdProtocol.ISO_15765_4_CAN_29BIT_500K
                or ObdProtocol.ISO_15765_4_CAN_29BIT_250K))
        {
            await onDiscovered(new DiscoveryResult(Array.Empty<ModuleLiveGroup>(),
                "ამ მანქანის კავშირის ტიპზე (არა CAN) მოდულების ცოცხალი მონაცემები მიუწვდომელია.",
                "Module live data is not available for this car's connection type (not CAN).")).ConfigureAwait(false);
            return;
        }

        var is29BitCar = protocol is ObdProtocol.ISO_15765_4_CAN_29BIT_500K
            or ObdProtocol.ISO_15765_4_CAN_29BIT_250K;

        await EnsureNamesLoadedAsync().ConfigureAwait(false);
        var requests = await LoadRequestsAsync(vehicle, is29BitCar, debugOnly, token).ConfigureAwait(false);
        if (requests.Count == 0)
        {
            await onDiscovered(new DiscoveryResult(Array.Empty<ModuleLiveGroup>(),
                "ამ მანქანისთვის მოდულების ცოცხალი მონაცემების აღწერა ბაზაში ჯერ არ არის.",
                "The database has no module live data definitions for this car yet.")).ConfigureAwait(false);
            return;
        }

        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            // 1. Find which requests the car answers.
            var working = new List<RequestDef>();
            var silent = new HashSet<string>(StringComparer.Ordinal);
            var alive = new HashSet<string>(StringComparer.Ordinal);
            var misses = new Dictionary<string, int>(StringComparer.Ordinal);
            // Research (debug=1) signals are largely unverified for this exact car: catalogs such as OBDb's Volt
            // repo share one signal file across both generations (2011-2015 and 2016-2019, very different
            // electronics — confirmed against generations.yaml), so the first DID tried for a module can easily be
            // one the car's actual generation never supported. One miss must not write off a module that answers
            // plenty of OTHER requests, the way it safely can for the already-reviewed (debug=0) signals below.
            var maxMissesPerModule = debugOnly ? 5 : 1;
            var stats = new ReadStats();
            for (var i = 0; i < requests.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report((i + 1d) / requests.Count);

                var request = requests[i];
                var module = $"{request.Tx:X}|{request.Ext29}|{request.Ext}";

                // A module that did not answer at all (maxMissesPerModule times in a row) is not asked again.
                if (silent.Contains(module))
                {
                    stats.SkippedSilent++;
                    continue;
                }

                var (data, anyReply) = await ReadAsync(session, request, token, stats).ConfigureAwait(false);
                if (anyReply)
                {
                    alive.Add(module);
                    misses.Remove(module);
                }
                else if (!alive.Contains(module))
                {
                    var count = misses.GetValueOrDefault(module) + 1;
                    misses[module] = count;
                    if (count >= maxMissesPerModule)
                        silent.Add(module);
                }

                if (data is null)
                    continue;

                var decoded = request.Signals
                    .Where(signal => TryFormat(signal, data, false, out _))
                    .ToList();
                if (decoded.Count == 0)
                    continue;

                request.Signals.RemoveAll(signal => !decoded.Contains(signal));
                working.Add(request);
            }

            var groups = BuildGroups(working);
            await onDiscovered(new DiscoveryResult(groups,
                groups.Count == 0
                    ? "მოდულებმა ამ მოთხოვნებზე პასუხი არ დააბრუნეს. ჩართე კონტაქტი (ან ძრავი) და სცადე თავიდან. [" + stats.Summary() + "]"
                    : string.Empty,
                groups.Count == 0
                    ? "The modules did not answer these requests. Turn the ignition (or engine) on and try again. [" + stats.Summary() + "]"
                    : string.Empty)).ConfigureAwait(false);

            if (groups.Count == 0)
                return;

            // 2. Read continuously while the page is open.
            var pass = 0;
            while (!token.IsCancellationRequested)
            {
                pass++;
                var english = IsEnglish();
                foreach (var request in working)
                {
                    token.ThrowIfCancellationRequested();
                    if (request.Failures >= 3 && pass % 10 != 0)
                        continue;

                    // Battery cell values change slowly: read them every 4th pass.
                    if (request.SlowChanging && pass > 1 && pass % 4 != 0)
                        continue;

                    var (data, _) = await ReadAsync(session, request, token).ConfigureAwait(false);
                    if (data is null)
                    {
                        request.Failures++;
                        if (request.Failures == 3)
                            SetValues(request.Signals.Select(signal => (signal, "—")).ToList());
                        continue;
                    }

                    request.Failures = 0;
                    var values = new List<(SignalDef, string)>();
                    foreach (var signal in request.Signals)
                        values.Add((signal, TryFormat(signal, data, english, out var text) ? text : "—"));
                    SetValues(values);
                }

                await Task.Delay(150, token).ConfigureAwait(false);
            }
        }, token).ConfigureAwait(false);
    }

    private static void SetValues(List<(SignalDef Def, string Text)> values)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var (def, text) in values)
            {
                if (def.Signal is not null)
                    def.Signal.ValueText = text;
            }
        });
    }

    private static async Task<(byte[]? Data, bool AnyReply)> ReadAsync(
        Elm327Protocol.IDtcModuleSession session,
        RequestDef request,
        CancellationToken token,
        ReadStats? stats = null)
    {
        stats ??= new ReadStats();
        stats.Sent++;
        var payload = request.Service == 0x21
            ? new[] { (byte)0x21, (byte)request.Did }
            : new[] { (byte)0x22, (byte)(request.Did >> 8), (byte)request.Did };

        try
        {
            var raw = await session.SendAsync(request.Tx, request.Rx, request.Ext29, payload, false, token, request.Ext).ConfigureAwait(false);
            var messages = ModuleDtcScanService.Reassemble(ModuleDtcScanService.StripExtended(
                ModuleDtcScanService.ParseFrames(raw, request.Rx, request.Ext29), request.Ext));

            if (messages.Count > 0 && messages.All(message =>
                    message.Length >= 3 && message[0] == 0x7F && message[1] == request.Service && message[2] == 0x78))
            {
                raw = await session.SendAsync(request.Tx, request.Rx, request.Ext29, payload, true, token, request.Ext).ConfigureAwait(false);
                messages = ModuleDtcScanService.Reassemble(ModuleDtcScanService.StripExtended(
                    ModuleDtcScanService.ParseFrames(raw, request.Rx, request.Ext29), request.Ext));
            }

            if (messages.Count == 0)
                stats.NoReply++;
            else if (messages.All(message => message.Length >= 3 && message[0] == 0x7F))
            {
                stats.Negative++;
                stats.NegativeCodes.Add(messages[0][2].ToString("X2"));
            }

            foreach (var message in messages)
            {
                if (request.Service == 0x21)
                {
                    if (message.Length >= 3 && message[0] == 0x61 && message[1] == (byte)request.Did)
                    {
                        stats.Positive++;
                        return (message[2..], true);
                    }
                }
                else if (message.Length >= 4 && message[0] == 0x62 &&
                         message[1] == (byte)(request.Did >> 8) && message[2] == (byte)request.Did)
                {
                {
                    stats.Positive++;
                    return (message[3..], true);
                }
                }
            }

            return (null, messages.Count > 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stats.Errors++;
            stats.FirstError ??= ex.Message.Length > 80 ? ex.Message[..80] : ex.Message;
            System.Diagnostics.Debug.WriteLine($"Module live data read failed: {ex.Message}");
        }

        return (null, false);
    }

    // ------------------------------------------------------------------
    // Loading definitions
    // ------------------------------------------------------------------

    private async Task<List<RequestDef>> LoadRequestsAsync(VehicleInfo vehicle, bool is29BitCar, bool debugOnly, CancellationToken token)
    {
        var makes = CatalogMakes(vehicle);
        if (makes.Length == 0)
            return new List<RequestDef>();

        await _catalog.EnsureLoadedAsync(token).ConfigureAwait(false);
        var connection = new SQLiteAsyncConnection(_catalog.DatabasePath, SQLiteOpenFlags.ReadOnly);
        var rows = new List<PidRow>();
        var wantedDebug = debugOnly ? 1 : 0;
        try
        {
            foreach (var make in makes)
            {
                rows.AddRange(await connection.QueryAsync<PidRow>(
                    """
                    SELECT
                        p.make AS Make,
                        p.model AS Model,
                        p.header AS Header,
                        p.command_json AS CommandJson,
                        p.name AS Name,
                        p.path AS Path,
                        p.unit AS Unit,
                        p.bit_length AS BitLength,
                        p.bit_offset AS BitOffset,
                        p.eax AS Eax,
                        p.format_json AS FormatJson,
                        p.raw_json AS RawJson
                    FROM pid_definitions p
                    WHERE lower(p.make) = lower(?)
                      AND p.pid IN ('22', '21')
                      AND p.debug = ?;
                    """,
                    make, wantedDebug).ConfigureAwait(false));
            }
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }

        rows = rows.Where(row =>
                !string.IsNullOrWhiteSpace(row.Name) &&
                !string.IsNullOrWhiteSpace(row.CommandJson) &&
                row.BitLength is >= 1 and <= 32 &&
                row.BitOffset >= 0 &&
                !IsExcludedPath(row.Path) &&
                !IsExcludedUnit(row.Unit))
            .ToList();

        var isGm = makes.Contains("Chevrolet", StringComparer.OrdinalIgnoreCase);
        var vehicleModel = AliasedModel(vehicle);

        // Exact model: equal names first; otherwise names where one starts with the other.
        var exactRows = new List<PidRow>();
        if (vehicleModel.Length >= 2 && vehicleModel != "unknown")
        {
            exactRows = rows.Where(row => NormalizeModel(row.Model) == vehicleModel).ToList();
            if (exactRows.Count == 0 && vehicleModel.Length >= 3)
            {
                exactRows = rows.Where(row =>
                {
                    var model = NormalizeModel(row.Model);
                    return model.Length >= 3 && (model.StartsWith(vehicleModel, StringComparison.Ordinal) ||
                                                 vehicleModel.StartsWith(model, StringComparison.Ordinal));
                }).ToList();
            }
        }

        var byKey = new Dictionary<string, RequestDef>(StringComparer.Ordinal);

        void AddRow(PidRow row, bool exact, int models)
        {
            if (!TryBuildRequest(row, isGm, is29BitCar, out var shape))
                return;

            if (!byKey.TryGetValue(shape.Key, out var request))
            {
                request = shape;
                request.Exact = exact;
                request.Models = models;
                byKey[shape.Key] = request;
            }
            else
            {
                request.Exact |= exact;
                request.Models = Math.Max(request.Models, models);
            }

            var name = row.Name!.Trim();
            var existing = request.Signals.FirstOrDefault(signal =>
                signal.BitOffset == row.BitOffset && signal.BitLength == row.BitLength);
            if (existing is not null)
            {
                if (NormalizeName(existing.Name) == NormalizeName(name) || existing.Exact)
                    return;

                // The same bits with a different meaning: the exact-model one wins;
                // two different brand-wide meanings are removed below as ambiguous.
                if (exact)
                    request.Signals.RemoveAll(signal => signal.BitOffset == row.BitOffset && signal.BitLength == row.BitLength);
            }

            var format = ParseFormat(row.FormatJson);
            if (format is null)
                return;

            request.Signals.Add(new SignalDef
            {
                Name = name,
                Path = row.Path ?? string.Empty,
                Unit = row.Unit ?? string.Empty,
                BitOffset = row.BitOffset,
                BitLength = row.BitLength,
                Format = format,
                Exact = exact
            });
        }

        foreach (var row in exactRows)
            AddRow(row, true, 1);

        // Brand-wide identifiers shared by at least two models of the make.
        var exactSet = new HashSet<PidRow>(exactRows);
        var shared = rows
            .Where(row => !exactSet.Contains(row))
            .GroupBy(row => $"{row.Header}|{row.Eax}|{row.CommandJson}|{row.BitOffset}|{row.BitLength}|{NormalizeName(row.Name)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => (Row: group.First(), Models: group.Select(row => NormalizeModel(row.Model)).Distinct().Count()))
            .Where(item => item.Models >= 2)
            .OrderByDescending(item => item.Models);
        foreach (var (row, models) in shared)
            AddRow(row, false, models);

        // Makes where the catalog describes (almost) only one model share nothing between two models. For such a
        // make the single-model signals of the same make are tried too; they are asked last and, as always, shown
        // only after a positive, identifier-matching reply with a value inside the valid range.
        const int EnoughBrandRequests = 40;
        if (byKey.Values.Count(request => !request.Exact && request.Signals.Count > 0) < EnoughBrandRequests)
        {
            var sharedKeys = new HashSet<string>(shared.Select(item => item.Row).Select(row =>
                $"{row.Header}|{row.Eax}|{row.CommandJson}|{row.BitOffset}|{row.BitLength}|{NormalizeName(row.Name)}"));
            foreach (var row in rows.Where(row => !exactSet.Contains(row)))
            {
                var key = $"{row.Header}|{row.Eax}|{row.CommandJson}|{row.BitOffset}|{row.BitLength}|{NormalizeName(row.Name)}";
                if (!sharedKeys.Contains(key))
                    AddRow(row, false, 1);
            }
        }

        // Remove ambiguous brand-wide signals (same bits, different names).
        foreach (var request in byKey.Values)
        {
            var ambiguous = request.Signals
                .Where(signal => !signal.Exact)
                .GroupBy(signal => (signal.BitOffset, signal.BitLength))
                .Where(group => group.Select(signal => NormalizeName(signal.Name)).Distinct().Count() > 1)
                .SelectMany(group => group)
                .ToList();
            request.Signals.RemoveAll(ambiguous.Contains);
        }

        // Per-cylinder signals (misfire counters, knock retard …) are often
        // shared brand-wide with V6/V8 models. Keep only the cylinders this
        // engine really has: the VIN-decoded count, otherwise the highest
        // cylinder defined for this exact model.
        var cylinders = EngineCylinderCount(vehicle.Engine);
        if (cylinders is null)
        {
            var exactMax = exactRows
                .Select(row => CylinderNumber($"{row.Name} {row.Path}"))
                .Where(number => number.HasValue)
                .Select(number => number!.Value)
                .DefaultIfEmpty(0)
                .Max();
            if (exactMax > 0)
                cylinders = exactMax;
        }

        if (cylinders is { } cylinderCount)
        {
            foreach (var request in byKey.Values)
                request.Signals.RemoveAll(signal => CylinderNumber($"{signal.Name} {signal.Path}") is { } number && number > cylinderCount);
        }

        var all = byKey.Values.Where(request => request.Signals.Count > 0).ToList();
        return all.Where(request => request.Exact)
            .OrderBy(request => request.Priority)
            .ThenBy(request => request.Did)
            .Take(MaxExactRequests)
            .Concat(all.Where(request => !request.Exact)
                .OrderByDescending(request => request.Models)
                .ThenBy(request => request.Priority)
                .ThenBy(request => request.Did)
                .Take(MaxBrandRequests))
            .ToList();
    }

    private static bool TryBuildRequest(PidRow row, bool isGm, bool is29BitCar, out RequestDef request)
    {
        request = null!;

        byte service;
        string? didText;
        try
        {
            using var doc = JsonDocument.Parse(row.CommandJson!);
            if (doc.RootElement.TryGetProperty("22", out var did22) && did22.ValueKind == JsonValueKind.String)
            {
                service = 0x22;
                didText = did22.GetString();
            }
            else if (doc.RootElement.TryGetProperty("21", out var did21) && did21.ValueKind == JsonValueKind.String)
            {
                service = 0x21;
                didText = did21.GetString();
            }
            else
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(didText) ||
            didText.Length != (service == 0x21 ? 2 : 4) ||
            !int.TryParse(didText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var did))
        {
            return false;
        }

        if (!ObdbDiagnosticAddressResolver.TryResolve(row.Header, row.Eax, row.RawJson, out var address, out _))
            return false;

        var tx = address.TransmitCanId;
        var rx = address.ReceiveCanId;
        var ext29 = address.Extended29Bit;

        // GM "Global A" cars (e.g. Volt 2011–2015) use 11-bit addresses for the
        // same modules that newer GM definitions list as 29-bit DAxx targets.
        if (isGm && !is29BitCar && ext29 && (tx & unchecked((int)0xFFFF00FF)) == 0x18DA00F1)
        {
            switch ((tx >> 8) & 0xFF)
            {
                case 0x11: tx = 0x7E0; rx = 0x7E8; break;
                case 0x18: tx = 0x7E2; rx = 0x7EA; break;
                case 0x28: tx = 0x243; rx = 0x643; break;
                default: return false;
            }

            ext29 = false;
        }

        if (!rx.HasValue)
        {
            if (ext29)
            {
                if ((tx & unchecked((int)0xFFFF0000)) != 0x18DA0000)
                    return false;
                rx = 0x18DA0000 | ((tx & 0xFF) << 8) | ((tx >> 8) & 0xFF);
            }
            else
            {
                rx = isGm && tx is >= 0x240 and <= 0x25F ? tx + 0x400 : tx + 8;
                if (rx > 0x7FF)
                    return false;
            }
        }

        request = new RequestDef
        {
            Tx = tx,
            Rx = rx.Value,
            Ext29 = ext29,
            Ext = address.ExtendedAddress,
            Service = service,
            Did = did
        };
        return true;
    }

    private static SignalFormat? ParseFormat(string? json)
    {
        var format = new SignalFormat();
        if (string.IsNullOrWhiteSpace(json))
            return format;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            format.Mul = Number(root, "mul");
            format.Div = Number(root, "div");
            format.Add = Number(root, "add");
            format.Min = Number(root, "min");
            format.Max = Number(root, "max");
            format.NullMin = Number(root, "nullmin");
            format.NullMax = Number(root, "nullmax");
            format.Sign = root.TryGetProperty("sign", out var sign) && sign.ValueKind == JsonValueKind.True;

            if (root.TryGetProperty("map", out var map) && map.ValueKind == JsonValueKind.Object)
            {
                format.Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in map.EnumerateObject())
                {
                    var text = entry.Value.ValueKind switch
                    {
                        JsonValueKind.Object when entry.Value.TryGetProperty("description", out var d) => d.GetString(),
                        JsonValueKind.Object when entry.Value.TryGetProperty("value", out var v) => v.GetString(),
                        JsonValueKind.String => entry.Value.GetString(),
                        _ => null
                    };
                    if (!string.IsNullOrWhiteSpace(text))
                        format.Map[entry.Name] = text;
                }
            }

            return format;
        }
        catch
        {
            return null;
        }
    }

    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;

    // ------------------------------------------------------------------
    // Decoding and text
    // ------------------------------------------------------------------

    private static bool TryDecode(SignalDef signal, byte[] data, out double value, out ulong raw)
    {
        value = 0;
        raw = 0;
        if (signal.BitOffset + signal.BitLength > data.Length * 8)
            return false;

        for (var bit = 0; bit < signal.BitLength; bit++)
        {
            var absolute = signal.BitOffset + bit;
            raw = (raw << 1) | (((ulong)data[absolute / 8] >> (7 - absolute % 8)) & 1UL);
        }

        double number = raw;
        if (signal.Format.Sign && signal.BitLength < 64 && (raw & (1UL << (signal.BitLength - 1))) != 0)
            number = (long)(raw | (ulong.MaxValue << signal.BitLength));

        if (signal.Format.Mul.HasValue) number *= signal.Format.Mul.Value;
        if (signal.Format.Div.HasValue)
        {
            if (Math.Abs(signal.Format.Div.Value) < double.Epsilon)
                return false;
            number /= signal.Format.Div.Value;
        }
        if (signal.Format.Add.HasValue) number += signal.Format.Add.Value;

        if (signal.Format.NullMin.HasValue && number <= signal.Format.NullMin.Value) return false;
        if (signal.Format.NullMax.HasValue && number >= signal.Format.NullMax.Value) return false;
        if (signal.Format.Min.HasValue && number < signal.Format.Min.Value) return false;
        if (signal.Format.Max.HasValue && number > signal.Format.Max.Value) return false;
        if (!double.IsFinite(number)) return false;

        value = number;
        return true;
    }

    private static bool TryFormat(SignalDef signal, byte[] data, bool english, out string text)
    {
        text = "—";
        if (!TryDecode(signal, data, out var value, out var raw))
            return false;

        if (signal.Format.Map is { Count: > 0 } map)
        {
            if (!map.TryGetValue(raw.ToString(CultureInfo.InvariantCulture), out var mapped))
                return false;
            text = english ? mapped : MapKa(mapped);
            return true;
        }

        switch (signal.Unit)
        {
            case "offon":
            case "onoff":
                text = value != 0 ? (english ? "On" : "ჩართული") : (english ? "Off" : "გამორთული");
                return true;
            case "noyes":
            case "yesno":
                text = value != 0 ? (english ? "Yes" : "კი") : (english ? "No" : "არა");
                return true;
            case "fahrenheit":
                value = (value - 32) * 5 / 9;
                break;
            case "miles":
            case "milesPerHour":
                value *= 1.609344;
                break;
            case "gallons":
                value *= 3.78541;
                break;
            case "psi":
                value *= 0.0689476;
                break;
        }

        text = FormatNumber(value, signal.Unit);
        return true;
    }

    private static string FormatNumber(double value, string unit)
    {
        if (unit == "psi")
            return value.ToString("0.00", CultureInfo.InvariantCulture);

        var abs = Math.Abs(value);
        var pattern = abs >= 1000 ? "0" : abs >= 100 ? "0.#" : abs >= 10 ? "0.##" : "0.###";
        return value.ToString(pattern, CultureInfo.InvariantCulture);
    }

    private static string MapKa(string text) => text.Trim().ToUpperInvariant() switch
    {
        "PARK" => "P (პარკინგი)",
        "NEUTRAL" => "N (ნეიტრალი)",
        "REVERSE" => "R (უკუსვლა)",
        "DRIVE" => "D (სვლა)",
        "ON" => "ჩართული",
        "OFF" => "გამორთული",
        "OPEN" => "ღია",
        "CLOSED" => "დახურული",
        "YES" => "კი",
        "NO" => "არა",
        "ACTIVE" => "აქტიური",
        "INACTIVE" => "არააქტიური",
        "LOCKED" => "ჩაკეტილი",
        "UNLOCKED" => "გაღებული",
        _ => text.StartsWith("Drive, gear ", StringComparison.OrdinalIgnoreCase)
            ? $"D{text["Drive, gear ".Length..]}"
            : text
    };

    private static (string Ka, string En) UnitText(string unit) => unit switch
    {
        "volts" => ("V", "V"),
        "celsius" or "fahrenheit" => ("°C", "°C"),
        "percent" => ("%", "%"),
        "kilometersPerHour" or "milesPerHour" => ("კმ/სთ", "km/h"),
        "degrees" => ("°", "°"),
        "kilometers" or "km" or "miles" => ("კმ", "km"),
        "bars" or "psi" => ("bar", "bar"),
        "rpm" => ("ბრ/წთ", "rpm"),
        "kilopascal" => ("kPa", "kPa"),
        "amps" => ("A", "A"),
        "millivolts" => ("mV", "mV"),
        "newtonMeters" => ("ნმ", "Nm"),
        "milliseconds" => ("მწმ", "ms"),
        "seconds" => ("წმ", "s"),
        "minutes" => ("წთ", "min"),
        "hours" => ("სთ", "h"),
        "liters" or "gallons" => ("ლ", "L"),
        "kilowattHours" => ("kWh", "kWh"),
        "kilowatts" => ("kW", "kW"),
        "watts" => ("W", "W"),
        "metersPerSecondSquared" => ("მ/წმ²", "m/s²"),
        "metersPerSecond" => ("მ/წმ", "m/s"),
        "millimeters" => ("მმ", "mm"),
        "centimeters" => ("სმ", "cm"),
        "meters" => ("მ", "m"),
        "ampereHours" => ("Ah", "Ah"),
        "milliamps" => ("mA", "mA"),
        "gravity" => ("g", "g"),
        "gramsPerSecond" => ("გ/წმ", "g/s"),
        "gramsPerLiter" => ("გ/ლ", "g/L"),
        "milligramsPerStroke" => ("მგ", "mg"),
        "hertz" => ("Hz", "Hz"),
        "kiloohms" => ("kΩ", "kΩ"),
        "ohms" => ("Ω", "Ω"),
        "milliohms" => ("mΩ", "mΩ"),
        "radians" => ("rad", "rad"),
        _ => (string.Empty, string.Empty)
    };

    // ------------------------------------------------------------------
    // Groups
    // ------------------------------------------------------------------

    private static readonly (string Id, string Ka, string En)[] GroupNames =
    {
        ("Movement", "მოძრაობა და ABS", "Movement and ABS"),
        ("Tires", "საბურავები", "Tires"),
        ("Engine", "ძრავი", "Engine"),
        ("Transmission", "გადაცემათა კოლოფი", "Transmission"),
        ("Battery", "ბატარეა", "Battery"),
        ("Battery.Cells", "ბატარეის უჯრედები", "Battery cells"),
        ("Battery.Modules", "ბატარეის მოდულები", "Battery modules"),
        ("Battery.Charging", "დამუხტვა", "Charging"),
        ("Fuel", "საწვავი", "Fuel"),
        ("Fluids", "სითხეები", "Fluids"),
        ("Electrical", "ელექტროსისტემა", "Electrical"),
        ("Drivetrain", "ტრანსმისია", "Drivetrain"),
        ("Orientation", "საჭე და მდებარეობა", "Steering and orientation"),
        ("Control", "მართვა", "Controls"),
        ("Cruise control", "კრუიზ-კონტროლი", "Cruise control"),
        ("Climate", "კლიმატი", "Climate"),
        ("Doors", "კარები", "Doors"),
        ("Seatbelts", "ღვედები", "Seatbelts"),
        ("Lights", "განათება", "Lights"),
        ("Emissions", "გამონაბოლქვი", "Emissions"),
        ("Trips", "მგზავრობა", "Trips"),
        ("Stats", "სტატისტიკა", "Statistics"),
        ("Clocks", "დრო", "Clocks"),
        ("Other", "სხვა", "Other")
    };

    private static int GroupOrder(string path)
    {
        var index = Array.FindIndex(GroupNames, group => group.Id == GroupIdOf(path));
        return index < 0 ? 99 : index;
    }

    private static string GroupIdOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Other";
        if (path.StartsWith("Battery.Cells", StringComparison.OrdinalIgnoreCase)) return "Battery.Cells";
        if (path.StartsWith("Battery.Modules", StringComparison.OrdinalIgnoreCase)) return "Battery.Modules";
        if (path.StartsWith("Battery.Charging", StringComparison.OrdinalIgnoreCase)) return "Battery.Charging";

        var top = path.Split('.')[0];
        return GroupNames.Any(group => group.Id.Equals(top, StringComparison.OrdinalIgnoreCase))
            ? GroupNames.First(group => group.Id.Equals(top, StringComparison.OrdinalIgnoreCase)).Id
            : "Other";
    }

    private static List<ModuleLiveGroup> BuildGroups(List<RequestDef> working)
    {
        var english = IsEnglish();
        var groups = new Dictionary<string, ModuleLiveGroup>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var request in working)
        {
            foreach (var def in request.Signals)
            {
                // The same value read from two places is shown once.
                if (!seen.Add($"{GroupIdOf(def.Path)}|{NormalizeName(def.Name)}"))
                    continue;

                var groupId = GroupIdOf(def.Path);
                if (!groups.TryGetValue(groupId, out var group))
                {
                    var index = Array.FindIndex(GroupNames, item => item.Id == groupId);
                    var names = GroupNames[Math.Max(0, index)];
                    group = new ModuleLiveGroup
                    {
                        Id = groupId,
                        TitleKa = names.Ka,
                        TitleEn = names.En,
                        Order = index < 0 ? 999 : index
                    };
                    groups[groupId] = group;
                }

                var (unitKa, unitEn) = UnitText(def.Unit);
                if (def.Format.Map is { Count: > 0 } || def.Unit is "offon" or "onoff" or "noyes" or "yesno")
                    (unitKa, unitEn) = (string.Empty, string.Empty);

                def.Signal = new ModuleLiveSignal
                {
                    Key = $"{request.Key}|{def.BitOffset}|{def.BitLength}",
                    GroupId = groupId,
                    NameEn = def.Name,
                    NameKa = NameKa(def.Name),
                    UnitKa = unitKa,
                    UnitEn = unitEn
                };
                group.Signals.Add(def.Signal);
            }
        }

        var result = groups.Values.OrderBy(group => group.Order).ToList();
        foreach (var group in result)
            group.ApplyLanguage(english);
        return result;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static async Task EnsureNamesLoadedAsync()
    {
        if (_namesKa is not null)
            return;

        await NamesGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_namesKa is not null)
                return;
            try
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync("module_signals_ka.json").ConfigureAwait(false);
                var names = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream).ConfigureAwait(false);
                _namesKa = new Dictionary<string, string>(names ?? new(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Module signal names not loaded: {ex.Message}");
                _namesKa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        finally
        {
            NamesGate.Release();
        }
    }

    private static string NameKa(string name) =>
        _namesKa is not null && _namesKa.TryGetValue(name.Trim(), out var ka) ? ka : string.Empty;

    private static bool IsEnglish() => CarScanner.Services.Localization.LanguageCatalog.UsesEnglishBase;

    private static bool IsExcludedPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (path.StartsWith("ECU", StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith("DTCs", StringComparison.OrdinalIgnoreCase));

    private static bool IsExcludedUnit(string? unit) =>
        unit is "hex" or "unknown";

    private static string NormalizeModel(string? model) =>
        new string((model ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// Model as the catalog names it: a BMW "330i" is a "3-Series" (catalog: 1-Series … 7-Series).
    /// Other models are compared by their letters and digits only.
    /// </summary>
    private static string AliasedModel(VehicleInfo vehicle)
    {
        var model = NormalizeModel(vehicle.Model);
        var make = (vehicle.Manufacturer ?? string.Empty).ToUpperInvariant();
        if (make.Contains("BMW", StringComparison.Ordinal) || make.Contains("BAYERISCHE", StringComparison.Ordinal))
        {
            var match = System.Text.RegularExpressions.Regex.Match(model, @"^([1-8])[0-9]{2}[a-z]{0,3}$");
            if (match.Success)
                return match.Groups[1].Value + "series";
        }

        return model;
    }

    private static string NormalizeName(string? name) =>
        new string((name ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string[] CatalogMakes(VehicleInfo vehicle)
    {
        var make = (vehicle.Manufacturer ?? string.Empty).Trim().ToUpperInvariant();
        bool Has(params string[] names) => names.Any(name => make.Contains(name, StringComparison.Ordinal));

        // Shared make families of the catalog (Peugeot/Citroën/Opel, Fiat/Alfa, Chrysler,
        // Mitsubishi, Lotus, smart …). Qualified: this method has the same name as the class.
        if (CarScanner.Services.CatalogMakes.Known(vehicle.Manufacturer))
            return CarScanner.Services.CatalogMakes.For(vehicle.Manufacturer).ToArray();

        if (Has("CHEVROLET", "GMC", "BUICK", "CADILLAC", "GENERAL MOTORS", "HOLDEN")) return new[] { "Chevrolet", "GMC" };
        if (Has("FORD", "LINCOLN")) return new[] { "Ford" };
        if (Has("MAZDA")) return new[] { "Mazda" };
        if (Has("KIA")) return new[] { "Kia", "Hyundai" };
        if (Has("HYUNDAI", "GENESIS")) return new[] { "Hyundai", "Kia" };
        if (Has("LEXUS")) return new[] { "Lexus", "Toyota" };
        if (Has("TOYOTA", "SCION")) return new[] { "Toyota", "Lexus" };
        if (Has("DAIHATSU")) return new[] { "Daihatsu" };
        if (Has("HONDA", "ACURA")) return new[] { "Honda", "Acura" };
        if (Has("PORSCHE")) return new[] { "Porsche" };
        if (Has("AUDI")) return new[] { "Audi", "Volkswagen" };
        if (Has("VOLKSWAGEN", "SKODA", "ŠKODA", "SEAT", "CUPRA")) return new[] { "Volkswagen", "Seat", "Audi" };
        if (Has("MINI")) return new[] { "MINI", "BMW" };
        if (Has("BMW")) return new[] { "BMW", "MINI" };
        if (Has("INFINITI", "NISSAN")) return new[] { "Nissan", "NissanInfiniti" };
        if (Has("SUBARU")) return new[] { "Subaru" };
        if (Has("JEEP", "DODGE", "RAM", "CHRYSLER")) return new[] { "Jeep", "Dodge", "Ram" };
        if (Has("RENAULT", "DACIA")) return new[] { "Renault", "Dacia" };
        if (Has("MERCEDES")) return new[] { "Mercedes" };
        if (Has("OPEL", "VAUXHALL")) return new[] { "VauxhallOpel" };
        if (Has("JAGUAR", "LAND ROVER")) return new[] { "Jaguar", "Land" };
        if (Has("VOLVO", "POLESTAR")) return new[] { "Volvo", "Polestar" };
        if (Has("HAVAL", "GREAT WALL")) return new[] { "Haval" };
        if (Has("SUZUKI")) return new[] { "Suzuki" };
        if (Has("ALFA")) return new[] { "Alfa" };
        if (Has("CITROEN", "CITROËN")) return new[] { "Citroen" };
        if (make is "MG" || make.StartsWith("MG ", StringComparison.Ordinal)) return new[] { "MG" };

        var vin = (vehicle.VIN ?? string.Empty).Trim().ToUpperInvariant();
        if (vin.Length >= 3)
        {
            var wmi = vin[..3];
            if (wmi is "1G1" or "1GC" or "1GT" or "1G4" or "1G6" or "2G1" or "3G1" or "1GN" or "1GK" or "KL1" or "KL8") return new[] { "Chevrolet", "GMC" };
            if (wmi.StartsWith("1F", StringComparison.Ordinal) || wmi.StartsWith("3F", StringComparison.Ordinal) || wmi == "WF0") return new[] { "Ford" };
            if (wmi.StartsWith("KN", StringComparison.Ordinal)) return new[] { "Kia", "Hyundai" };
            if (wmi.StartsWith("KM", StringComparison.Ordinal) || wmi is "5NP" or "5XY" or "5NM") return new[] { "Hyundai", "Kia" };
            if (wmi.StartsWith("JT", StringComparison.Ordinal) || wmi is "4T1" or "4T3" or "5TD" or "5TF" or "2T1") return new[] { "Toyota", "Lexus" };
            if (wmi is "WBA" or "WBS" or "WBY" or "5UX") return new[] { "BMW", "MINI" };
            if (wmi is "WMW") return new[] { "MINI", "BMW" };
            if (wmi is "WVW" or "WVG" or "3VW" or "1VW") return new[] { "Volkswagen", "Seat", "Audi" };
            if (wmi is "WAU" or "WA1") return new[] { "Audi", "Volkswagen" };
            if (wmi is "WP0" or "WP1") return new[] { "Porsche" };
        }

        return Array.Empty<string>();
    }
}
