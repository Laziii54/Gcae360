using System.Globalization;
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

    /// <summary>Text form, e.g. "svc=22;tx=7E0;rx=7E8;e29=0;id=1A0;off=0;len=4;le=0;sc=0.015625;mi=0;mod=IPC".</summary>
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
        var km = raw * Scale * (Miles ? MileagePlausibility.KmPerMile : 1);
        return MileagePlausibility.IsPlausibleOdometerKm(km) && km >= 1 ? km : null;
    }
}

/// <summary>
/// Reads odometer places that were saved on THIS phone earlier. The app no longer searches for new ones: the former
/// "odometer finder" tried hundreds of guessed identifiers on every module, which is not a documented read-only
/// procedure, so it was removed. Places are never fetched from or sent to a server.
/// </summary>
public sealed class OdometerFinderService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly OBDService _obd;

    public OdometerFinderService(OBDService obd)
    {
        _obd = obd;
    }

    // ------------------------------------------------------------------
    // Reading a saved place
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

    // ------------------------------------------------------------------
    // Places saved on this phone
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

    /// <summary>Places saved on this phone for this exact make / model / year (requires all three to be known).</summary>
    public Task<IReadOnlyList<OdometerLocation>> KnownAsync(VehicleInfo vehicle, CancellationToken token)
    {
        var list = new List<OdometerLocation>();
        if (HasModel(vehicle) && LoadLocal().TryGetValue(ModelKey(vehicle), out var local))
        {
            foreach (var key in local.Distinct())
            {
                if (OdometerLocation.TryParse(key, out var location) &&
                    !list.Any(l => l.Tx == location.Tx && l.Service == location.Service && l.Did == location.Did))
                    list.Add(location);
            }
        }

        return Task.FromResult<IReadOnlyList<OdometerLocation>>(list);
    }

    private static bool HasModel(VehicleInfo vehicle) =>
        Known(vehicle.Manufacturer) && Known(vehicle.Model) && Known(vehicle.Year);

    private static bool Known(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Odometer from places saved on this phone earlier. Unverified: the places were not independently documented.</summary>
public sealed class LearnedOdometerProvider : IActualMileageProvider
{
    private readonly OdometerFinderService _finder;

    public LearnedOdometerProvider(OdometerFinderService finder) => _finder = finder;

    public string Name => "Saved odometer place (this phone)";

    public async Task<IReadOnlyList<MileageEvidence>> ReadAllAsync(VehicleInfo vehicle, CancellationToken cancellationToken = default)
    {
        var locations = await _finder.KnownAsync(vehicle, cancellationToken).ConfigureAwait(false);
        var assessment = MileageSourceCompatibility.Evaluate(
            MileageSourceFamily.LearnedLocation,
            new MileageSourceFacts(true, true, true, true, null, 0, false, locations.Count));

        if (!assessment.ShouldTry)
        {
            MileageTrace.Attempt(new MileageAttempt(Name, "saved place", MileageAttemptOutcome.NotTried, string.Empty, assessment.Status, assessment.Reason));
            return Array.Empty<MileageEvidence>();
        }

        var values = await _finder.ReadAsync(locations, cancellationToken).ConfigureAwait(false);
        foreach (var (location, km) in values)
            MileageTrace.Add($"{location.Module} {location.RequestText}: {km:0} km");

        if (values.Count == 0)
            MileageTrace.Attempt(new MileageAttempt(Name, "saved place", MileageAttemptOutcome.ModuleNotResponding, string.Empty, assessment.Status, assessment.Reason));
        foreach (var (location, _) in values)
            MileageTrace.Attempt(new MileageAttempt(Name, $"{location.RequestText} @ 0x{location.Tx:X}", MileageAttemptOutcome.Read, string.Empty, assessment.Status, assessment.Reason));

        return values
            .Select(v => new MileageEvidence(Name, v.Location.Module, Math.Round(v.Km, 1), 0.5,
                $"{v.Location.RequestText} · TX 0x{v.Location.Tx:X} · bytes {v.Location.Offset}+{v.Location.Length}")
            {
                Measurement = MileageMeasurement.TotalOdometer,
                Unit = v.Location.Miles ? "mi" : "km",
                RawValue = v.Location.Miles ? v.Km / MileagePlausibility.KmPerMile : v.Km,
                SourceKind = MileageSourceKind.LearnedOnPhone,
                Method = $"{v.Location.RequestText} @ 0x{v.Location.Tx:X}"
            })
            .ToList();
    }
}
