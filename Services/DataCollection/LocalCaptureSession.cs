using System.Text.Json;
using System.Text.Json.Serialization;
using CarScanner.Services.OBD;

namespace CarScanner.Services.DataCollection;

public sealed record CaptureVehicle(string? Make, string? Model, string? Year, string? Protocol);

/// <summary>One kept observation. The vehicle is described by make/model/year/protocol only, never by a VIN.</summary>
public sealed record CaptureItem(
    string Make, string Model, string Year, string Protocol,
    int Tx, int Rx, bool Ext29, string Request, string Response, string ObservedOn);

/// <summary>How many requests of one addressed module got frames back. A frame is not proof of a valid UDS answer.</summary>
public sealed record CaptureModuleSummary(string Tx, string Rx, bool Ext29, int Requests, int WithFrames, int StatusOnly);

public sealed record CaptureCounts(int Observed, int Kept, int Duplicates, int Rejected, int NoVehicle, int DroppedFull);

public sealed record CaptureReport(
    string Schema,
    string CreatedOn,
    string AppVersion,
    string? TargetId,
    string EvidenceLevel,
    string Notice,
    CaptureCounts Counts,
    IReadOnlyList<CaptureModuleSummary> Modules,
    IReadOnlyList<CaptureItem> Items);

/// <summary>
/// A user-started, local-only recording of what the app already exchanged with the car. It reuses the exact
/// sanitising rules of the optional upload (read-only services only, no VIN, no serial numbers, standard OBD2
/// reduced to compatibility summaries) and never sends anything anywhere: the result is a file the user can
/// review and share on purpose.
///
/// It only observes. <see cref="Observe"/> is cheap, takes a short lock, never throws and never touches the
/// adapter, so a recording cannot slow down or break normal diagnostics.
/// </summary>
public sealed class LocalCaptureSession
{
    public const int MaxItems = 3000;
    public const string SchemaId = "gcar360.capture/1";

    /// <summary>The recording is raw material. Nothing in it is "supported" until someone reviews it against documentation.</summary>
    public const string EvidenceLevel = "UnreviewedObservation";

    public const string Notice =
        "Raw read-only exchanges observed by the app on one vehicle. This is NOT verification: a reply does not " +
        "prove what a request means, and no feature is supported because it appears here. No VIN, serial number or " +
        "account data is included. The file was never uploaded by the app.";

    private readonly object _gate = new();
    private readonly Dictionary<string, CaptureItem> _items = new();
    private readonly List<string> _order = new();
    private Func<CaptureVehicle?> _vehicle = () => null;
    private Func<DateTimeOffset> _clock = () => DateTimeOffset.UtcNow;
    private string? _targetId;
    private int _observed, _duplicates, _rejected, _noVehicle, _droppedFull;

    public bool IsRecording { get; private set; }

    public int ItemCount
    {
        get { lock (_gate) return _items.Count; }
    }

    /// <summary>Starts a new recording; anything recorded before is discarded.</summary>
    public void Start(string? targetId, Func<CaptureVehicle?> vehicle, Func<DateTimeOffset>? clock = null)
    {
        lock (_gate)
        {
            _items.Clear();
            _order.Clear();
            _observed = _duplicates = _rejected = _noVehicle = _droppedFull = 0;
            _targetId = string.IsNullOrWhiteSpace(targetId) ? null : targetId.Trim();
            _vehicle = vehicle ?? (() => null);
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            IsRecording = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
            IsRecording = false;
    }

    public void Clear()
    {
        lock (_gate)
        {
            IsRecording = false;
            _items.Clear();
            _order.Clear();
            _observed = _duplicates = _rejected = _noVehicle = _droppedFull = 0;
        }
    }

    /// <summary>Hand-over from <see cref="ObdExchangeTap"/>. Never throws.</summary>
    public void Observe(ObdRawExchange exchange)
    {
        try
        {
            if (!IsRecording || exchange is null)
                return;

            var vehicle = _vehicle();
            var now = _clock();
            lock (_gate)
            {
                if (!IsRecording)
                    return;

                _observed++;
                if (vehicle is null || string.IsNullOrWhiteSpace(vehicle.Make) || string.IsNullOrWhiteSpace(vehicle.Model))
                {
                    _noVehicle++;
                    return;
                }

                var queued = CollectionIntake.TryCreate(exchange, vehicle.Make, vehicle.Model, vehicle.Year, vehicle.Protocol, now);
                if (queued is null)
                {
                    _rejected++;
                    return;
                }

                if (_items.ContainsKey(queued.Key))
                {
                    _duplicates++;
                    return;
                }

                if (_items.Count >= MaxItems)
                {
                    _droppedFull++;
                    return;
                }

                var e = queued.Exchange;
                _items[queued.Key] = new CaptureItem(queued.Make, queued.Model, queued.Year, queued.Protocol,
                    e.TxCanId, e.RxCanId, e.Extended29Bit, e.Request, e.Response, queued.ObservedOn ?? string.Empty);
                _order.Add(queued.Key);
            }
        }
        catch
        {
            // recording must never affect diagnostics
        }
    }

    public CaptureReport BuildReport(string appVersion)
    {
        lock (_gate)
        {
            var items = _order.Select(k => _items[k]).ToArray();
            var modules = items
                .Where(i => i.Rx != 0) // standard OBD2 summaries are addressed to the functional id, not a module
                .GroupBy(i => (i.Tx, i.Rx, i.Ext29))
                .Select(g => new CaptureModuleSummary(
                    g.Key.Tx.ToString("X"), g.Key.Rx.ToString("X"), g.Key.Ext29,
                    g.Count(),
                    g.Count(i => HasFrames(i.Response)),
                    g.Count(i => !HasFrames(i.Response))))
                .OrderBy(m => m.Tx, StringComparer.Ordinal)
                .ToArray();

            return new CaptureReport(
                SchemaId,
                _clock().UtcDateTime.ToString("yyyy-MM-dd"),
                appVersion ?? string.Empty,
                _targetId,
                EvidenceLevel,
                Notice,
                new CaptureCounts(_observed, items.Length, _duplicates, _rejected, _noVehicle, _droppedFull),
                modules,
                items);
        }
    }

    public string BuildJson(string appVersion) =>
        JsonSerializer.Serialize(BuildReport(appVersion), new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

    // A frame line is hex (CAN id + data); status words such as NO DATA are not frames.
    private static bool HasFrames(string response) =>
        response.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(l => l.All(Uri.IsHexDigit));
}
