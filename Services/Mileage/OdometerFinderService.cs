using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using CarScanner.Models;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Services.Mileage;

/// <summary>
/// Where one module keeps the odometer: request (service + identifier), the
/// bytes of the answer and the scale. Read-only (0x22 / GMLAN 0x1A).
/// </summary>
public sealed record OdometerLocation(
    string Module,
    int Tx,
    int Rx,
    bool Ext29,
    byte Service,
    int Did,
    int Offset,
    int Length,
    bool LittleEndian,
    double Scale,
    bool Miles)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Text form shared with the server, e.g. "svc=22;tx=7E0;rx=7E8;e29=0;id=1A0;off=0;len=4;le=0;sc=0.015625;mi=0;mod=IPC".</summary>
    public string Key =>
        $"svc={Service:X2};tx={Tx:X};rx={Rx:X};e29={(Ext29 ? 1 : 0)};id={Did:X};off={Offset};len={Length};le={(LittleEndian ? 1 : 0)};" +
        $"sc={Scale.ToString("R", Inv)};mi={(Miles ? 1 : 0)};mod={Uri.EscapeDataString(Module)}";

    public string RequestText => Service == 0x1A ? $"1A {Did:X2}" : $"22 {Did:X4}";

    public static bool TryParse(string? key, out OdometerLocation location)
    {
        location = null!;
        if (string.IsNullOrWhiteSpace(key) || key.Length > 400)
            return false;
        try
        {
            var parts = key.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(pair => pair.Length == 2)
                .ToDictionary(pair => pair[0], pair => pair[1]);
            int Hex(string name) => int.Parse(parts[name], NumberStyles.HexNumber, Inv);
            int Int(string name) => int.Parse(parts[name], NumberStyles.Integer, Inv);
            var service = (byte)Hex("svc");
            if (service is not (0x22 or 0x1A))
                return false;
            var length = Int("len");
            var scale = double.Parse(parts["sc"], NumberStyles.Float, Inv);
            if (length is < 2 or > 4 || scale <= 0 || scale > 100)
                return false;
            location = new OdometerLocation(
                parts.TryGetValue("mod", out var module) ? Uri.UnescapeDataString(module) : "ECU",
                Hex("tx"), Hex("rx"), Int("e29") == 1, service, Hex("id"),
                Int("off"), length, Int("le") == 1, scale, Int("mi") == 1);
            return location.Tx > 0 && location.Rx > 0 && location.Offset is >= 0 and < 64;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Kilometres from the data bytes after "62 DID" / "5A DID".</summary>
    public double? Decode(byte[] data)
    {
        if (Offset + Length > data.Length)
            return null;
        ulong raw = 0;
        for (var i = 0; i < Length; i++)
        {
            var b = data[Offset + (LittleEndian ? Length - 1 - i : i)];
            raw = (raw << 8) | b;
        }
        var km = raw * Scale * (Miles ? 1.609344 : 1);
        return km is >= 1 and <= 3_000_000 ? km : null;
    }
}

public sealed record OdometerMatch(OdometerLocation Location, double ValueKm);

/// <summary>
/// "Odometer finder": the user types the dashboard distance once, the app reads
/// identifiers of the car's modules (read-only) and looks for that number in
/// the answers. A found place is saved for this model on the phone and shared
/// with the server (only the place — never the distance or the full VIN), so
/// the same model then works for everyone without typing.
/// </summary>
public sealed class OdometerFinderService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // Identifiers to try with 0x22 on every module: known odometer identifiers
    // of several makes, then a short range most makes use for vehicle data.
    private static readonly int[] UdsCandidates =
        new[] { 0xDD01, 0xDD00, 0xDD02, 0xDD04, 0xF4A6, 0x448F, 0x4490, 0x0286, 0x02BD, 0x2203, 0x1001, 0xF1B0, 0xB002, 0x0102 }
            .Concat(Enumerable.Range(0x0000, 0x200))
            .Distinct()
            .ToArray();

    private static readonly (double Scale, bool Miles)[] Scales =
    {
        (1, false), (0.1, false), (0.01, false), (1d / 64, false), (0.125, false), (0.5, false), (10, false),
        (1, true), (0.1, true), (1d / 64, true)
    };

    private readonly OBDService _obd;
    private readonly ModuleDtcScanService _modules;
    private readonly AccountService _account;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly Dictionary<string, (DateTime At, IReadOnlyList<OdometerLocation> List)> _serverCache = new();

    public OdometerFinderService(OBDService obd, ModuleDtcScanService modules, AccountService account)
    {
        _obd = obd;
        _modules = modules;
        _account = account;
    }

    // ------------------------------------------------------------------
    // Search
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<OdometerMatch>> SearchAsync(
        VehicleInfo vehicle,
        double dashboardKm,
        IProgress<(double Fraction, string Text)>? progress,
        CancellationToken token)
    {
        if (!_obd.IsInitialized)
            await _obd.InitializeAsync(token).ConfigureAwait(false);
        if (!_obd.IsCanProtocol)
            throw new InvalidOperationException(ObdLang.L("ძებნა მხოლოდ CAN მანქანებზე მუშაობს (დაახლ. 2008 წლიდან).", "The search works only on CAN cars (about 2008 and newer)."));

        var modules = _modules.LastModules.Where(m => m.Responded).ToList();
        if (modules.Count == 0)
        {
            progress?.Report((0.02, ObdLang.L("მოდულების მოძებნა…", "Finding the modules…")));
            var speed = await ReadSpeedAsync(token).ConfigureAwait(false);
            var outcome = await _modules.ScanAsync(vehicle, speed,
                new Progress<(double Fraction, string Text)>(p => progress?.Report((p.Fraction * 0.15, p.Text))), token).ConfigureAwait(false);
            modules = outcome.Modules.Where(m => m.Responded).ToList();
        }

        modules = modules
            .Where(m => !m.ExtendedAddress.HasValue)
            .OrderBy(Priority)
            .ThenBy(m => m.TransmitCanId)
            .Take(10)
            .ToList();
        if (modules.Count == 0)
            throw new InvalidOperationException(ObdLang.L("მოდულები ვერ მოიძებნა. ჩართეთ ანთება და სცადეთ თავიდან.", "No modules answered. Switch the ignition on and try again."));

        var matches = new List<OdometerMatch>();
        for (var index = 0; index < modules.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var module = modules[index];
            var name = ModuleTitle(module);
            var start = 0.15 + 0.85 * index / modules.Count;
            var span = 0.85 / modules.Count;

            var plan = new List<(byte Service, int Did)>();
            if (string.Equals(module.Protocol, "GMLAN", StringComparison.OrdinalIgnoreCase))
                plan.AddRange(Enumerable.Range(0x00, 0x100).Select(did => ((byte)0x1A, did)));
            plan.AddRange(UdsCandidates.Select(did => ((byte)0x22, did)));

            var found = await SearchModuleAsync(module, name, plan, dashboardKm,
                (done, total) => progress?.Report((start + span * done / total, ObdLang.L($"{name}: {done}/{total}", $"{name}: {done}/{total}"))),
                token).ConfigureAwait(false);
            matches.AddRange(found);
        }

        progress?.Report((1, string.Empty));
        return matches
            .GroupBy(m => (m.Location.Tx, m.Location.Service, m.Location.Did))
            .Select(g => g.OrderBy(m => Math.Abs(m.ValueKm - dashboardKm)).ThenByDescending(m => m.Location.Length).First())
            .OrderBy(m => Math.Abs(m.ValueKm - dashboardKm))
            .ToList();
    }

    private async Task<List<OdometerMatch>> SearchModuleAsync(
        ModuleDtcReport module,
        string name,
        List<(byte Service, int Did)> plan,
        double dashboardKm,
        Action<int, int> report,
        CancellationToken token)
    {
        var matches = new List<OdometerMatch>();
        var silentInRow = 0;
        byte? skipService = null;
        byte? lastService = null;

        // Short sessions (64 requests) so other parts of the app are not blocked for minutes.
        for (var batchStart = 0; batchStart < plan.Count; batchStart += 64)
        {
            token.ThrowIfCancellationRequested();
            var batch = plan.Skip(batchStart).Take(64).ToList();
            await _obd.RunDtcModuleSessionAsync(async session =>
            {
                foreach (var (service, did) in batch)
                {
                    token.ThrowIfCancellationRequested();
                    if (skipService == service)
                        continue;
                    if (lastService != service)
                    {
                        lastService = service;
                        silentInRow = 0;
                    }

                    var reply = await RequestAsync(session, module, service, did, token).ConfigureAwait(false);
                    if (reply.Silent)
                    {
                        // A module that ignores unknown identifiers: stop this service after 24 silent tries.
                        if (++silentInRow >= 24 && matches.Count == 0)
                            skipService = service;
                        continue;
                    }
                    silentInRow = 0;
                    if (reply.Data is not { Length: >= 2 and <= 64 } data)
                        continue;

                    foreach (var location in Candidates(module, name, service, did, data, dashboardKm))
                    {
                        // Read once more: the value must be stable.
                        var again = await RequestAsync(session, module, service, did, token).ConfigureAwait(false);
                        if (again.Data is { } second && location.Decode(second) is { } km2 && IsClose(km2, dashboardKm))
                        {
                            matches.Add(new OdometerMatch(location, km2));
                            break;
                        }
                    }
                }
            }, token).ConfigureAwait(false);

            report(Math.Min(plan.Count, batchStart + batch.Count), plan.Count);
            if (skipService.HasValue && plan.Skip(batchStart + 64).All(p => p.Service == skipService))
                break;
        }
        return matches;
    }

    private static bool IsClose(double km, double dashboardKm) =>
        Math.Abs(km - dashboardKm) <= Math.Max(3, dashboardKm * 0.004);

    /// <summary>Every byte window × scale whose value equals the dashboard distance.</summary>
    private static IEnumerable<OdometerLocation> Candidates(ModuleDtcReport module, string name, byte service, int did, byte[] data, double dashboardKm)
    {
        var list = new List<(OdometerLocation Location, double Diff)>();
        foreach (var length in new[] { 4, 3, 2 })
        {
            for (var offset = 0; offset + length <= data.Length; offset++)
            {
                foreach (var little in new[] { false, true })
                {
                    foreach (var (scale, miles) in Scales)
                    {
                        // 2-byte values only without a fraction (avoid chance matches).
                        if (length == 2 && scale < 1)
                            continue;
                        var location = new OdometerLocation(name, module.TransmitCanId, module.ReceiveCanId, module.Extended29Bit,
                            service, did, offset, length, little, scale, miles);
                        if (location.Decode(data) is { } km && km >= 50 && IsClose(km, dashboardKm))
                            list.Add((location, Math.Abs(km - dashboardKm)));
                    }
                }
            }
        }
        return list.OrderBy(item => item.Diff).ThenByDescending(item => item.Location.Length).Select(item => item.Location).Take(3);
    }

    private sealed record Reply(byte[]? Data, bool Silent);

    private static async Task<Reply> RequestAsync(Elm327Protocol.IDtcModuleSession session, ModuleDtcReport module, byte service, int did, CancellationToken token)
    {
        var request = service == 0x1A
            ? new byte[] { 0x1A, (byte)did }
            : new byte[] { 0x22, (byte)(did >> 8), (byte)did };
        string raw;
        try
        {
            raw = await session.SendAsync(module.TransmitCanId, module.ReceiveCanId, module.Extended29Bit, request, longWait: false, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new Reply(null, true);
        }

        var messages = ModuleDtcScanService.Reassemble(ModuleDtcScanService.ParseFrames(raw, module.ReceiveCanId, module.Extended29Bit));
        if (messages.Count == 0)
            return new Reply(null, true);

        var idLength = service == 0x1A ? 1 : 2;
        var positive = messages.FirstOrDefault(m =>
            m.Length > 1 + idLength && m[0] == service + 0x40 &&
            (service == 0x1A ? m[1] == (byte)did : m[1] == (byte)(did >> 8) && m[2] == (byte)did));
        return new Reply(positive?.Skip(1 + idLength).ToArray(), false);
    }

    private static int Priority(ModuleDtcReport module)
    {
        var text = $"{module.ModuleId} {module.NameEn} {module.EcuName}".ToUpperInvariant();
        if (text.Contains("IPC") || text.Contains("CLUSTER") || text.Contains("INSTRUMENT") || text.Contains("KOMBI") || text.Contains("DASH")) return 0;
        if (text.Contains("BCM") || text.Contains("BODY")) return 1;
        if (text.Contains("ECM") || text.Contains("PCM") || text.Contains("ENGINE")) return 2;
        if (text.Contains("HPCM") || text.Contains("HYBRID") || text.Contains("BECM") || text.Contains("BATTERY")) return 3;
        if (text.Contains("TCM") || text.Contains("TRANS")) return 4;
        if (text.Contains("GW") || text.Contains("GATEWAY")) return 5;
        return 6;
    }

    private static string ModuleTitle(ModuleDtcReport module)
    {
        var name = ObdLang.IsEnglish ? module.NameEn : module.NameKa;
        if (string.IsNullOrWhiteSpace(name)) name = module.ModuleId;
        return $"{name} (0x{module.TransmitCanId:X})";
    }

    private async Task<double?> ReadSpeedAsync(CancellationToken token)
    {
        try
        {
            var value = await _obd.ReadSensorAsync(0x0D, token).ConfigureAwait(false);
            return value is not null && double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Reading a known place
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<(OdometerLocation Location, double Km)>> ReadAsync(
        IReadOnlyList<OdometerLocation> locations,
        CancellationToken token)
    {
        var results = new List<(OdometerLocation, double)>();
        if (locations.Count == 0 || !_obd.IsInitialized || !_obd.IsCanProtocol)
            return results;

        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            foreach (var location in locations.Take(8))
            {
                token.ThrowIfCancellationRequested();
                var module = new ModuleDtcReport
                {
                    ModuleId = location.Module,
                    TransmitCanId = location.Tx,
                    ReceiveCanId = location.Rx,
                    Extended29Bit = location.Ext29
                };
                try
                {
                    var reply = await RequestAsync(session, module, location.Service, location.Did, token).ConfigureAwait(false);
                    if (reply.Data is { } data && location.Decode(data) is { } km)
                        results.Add((location, km));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // One module that does not answer does not stop the others.
                }
            }
        }, token).ConfigureAwait(false);
        return results;
    }

    // ------------------------------------------------------------------
    // Places for this model: on the phone + confirmed on the server
    // ------------------------------------------------------------------

    private static string ModelKey(VehicleInfo vehicle) =>
        string.Join("|", new[] { vehicle.Manufacturer, vehicle.Model, vehicle.Year }
            .Select(v => new string((v ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray())));

    private static string LocalPath => Path.Combine(FileSystem.AppDataDirectory, "odometer_locations.json");

    private static Dictionary<string, List<string>> LoadLocal()
    {
        try
        {
            return File.Exists(LocalPath)
                ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(LocalPath)) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    public void SaveLocal(VehicleInfo vehicle, IEnumerable<OdometerLocation> locations)
    {
        var all = LoadLocal();
        var key = ModelKey(vehicle);
        if (!all.TryGetValue(key, out var list))
            all[key] = list = new List<string>();
        foreach (var location in locations)
        {
            if (!list.Contains(location.Key))
                list.Add(location.Key);
        }
        File.WriteAllText(LocalPath, JsonSerializer.Serialize(all, Json));
        lock (_serverCache) _serverCache.Remove(key);
    }

    public async Task<IReadOnlyList<OdometerLocation>> KnownAsync(VehicleInfo vehicle, CancellationToken token)
    {
        var keys = new List<string>();
        if (LoadLocal().TryGetValue(ModelKey(vehicle), out var local))
            keys.AddRange(local);
        keys.AddRange((await ServerKnownAsync(vehicle, token).ConfigureAwait(false)).Select(l => l.Key));

        var list = new List<OdometerLocation>();
        foreach (var key in keys.Distinct())
        {
            if (OdometerLocation.TryParse(key, out var location) && !list.Any(l => l.Tx == location.Tx && l.Service == location.Service && l.Did == location.Did))
                list.Add(location);
        }
        return list;
    }

    private async Task<IReadOnlyList<OdometerLocation>> ServerKnownAsync(VehicleInfo vehicle, CancellationToken token)
    {
        if (!HasModel(vehicle))
            return Array.Empty<OdometerLocation>();
        var cacheKey = ModelKey(vehicle);
        lock (_serverCache)
        {
            if (_serverCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromHours(6))
                return cached.List;
        }

        try
        {
            var path = "/api/odometer/known?make=" + Uri.EscapeDataString(vehicle.Manufacturer) +
                       "&model=" + Uri.EscapeDataString(vehicle.Model) +
                       "&year=" + Uri.EscapeDataString(vehicle.Year);
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Get, path);
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Array.Empty<OdometerLocation>();
            var body = await response.Content.ReadFromJsonAsync<KnownResponse>(cancellationToken: token).ConfigureAwait(false);
            var list = (body?.Locations ?? Array.Empty<string>())
                .Select(key => OdometerLocation.TryParse(key, out var l) ? l : null)
                .OfType<OdometerLocation>()
                .ToList();
            lock (_serverCache) _serverCache[cacheKey] = (DateTime.UtcNow, list);
            return list;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<OdometerLocation>();
        }
    }

    /// <summary>Shares the found places (never the distance or the full VIN).</summary>
    public async Task<bool> ReportAsync(VehicleInfo vehicle, IReadOnlyList<OdometerLocation> locations)
    {
        if (!HasModel(vehicle) || locations.Count == 0)
            return false;
        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/odometer/report");
            request.Content = JsonContent.Create(new
            {
                make = vehicle.Manufacturer,
                model = vehicle.Model,
                year = vehicle.Year,
                vinPrefix = vehicle.VIN is { Length: 17 } vin ? vin[..11] : null,
                appVersion = AppInfo.Current.VersionString,
                locations = locations.Take(10).Select(l => l.Key).ToArray()
            });
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasModel(VehicleInfo vehicle) =>
        Known(vehicle.Manufacturer) && Known(vehicle.Model) && Known(vehicle.Year);

    private static bool Known(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

    private sealed record KnownResponse(string[]? Locations);
}

/// <summary>Odometer from the places found by the finder (this phone or confirmed by others).</summary>
public sealed class LearnedOdometerProvider : IActualMileageProvider
{
    private readonly OdometerFinderService _finder;

    public LearnedOdometerProvider(OdometerFinderService finder) => _finder = finder;

    public string Name => "Learned odometer";

    public async Task<IReadOnlyList<MileageEvidence>> ReadAllAsync(VehicleInfo vehicle, CancellationToken cancellationToken = default)
    {
        var locations = await _finder.KnownAsync(vehicle, cancellationToken).ConfigureAwait(false);
        MileageTrace.Add(locations.Count == 0
            ? "ნაპოვნი/დადასტურებული ადგილი ამ მოდელზე ჯერ არ არის"
            : $"ცნობილი ადგილები: {locations.Count}");
        if (locations.Count == 0)
            return Array.Empty<MileageEvidence>();

        var values = await _finder.ReadAsync(locations, cancellationToken).ConfigureAwait(false);
        foreach (var (location, km) in values)
            MileageTrace.Add($"{location.Module} {location.RequestText}: {km:0} km ✓");

        return values
            .Select(v => new MileageEvidence(Name, v.Location.Module, Math.Round(v.Km, 1), 0.8,
                $"{v.Location.RequestText} · TX 0x{v.Location.Tx:X} · bytes {v.Location.Offset}+{v.Location.Length}"))
            .ToList();
    }
}
