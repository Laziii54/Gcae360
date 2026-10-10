using System.Text.Json;
using CarScanner.Services.OBD;

namespace CarScanner.Services.DataCollection;

/// <summary>
/// An exchange waiting to be uploaded, with the (non-identifying) vehicle description.
/// <see cref="ObservedOn"/> is the UTC date (yyyy-MM-dd) it was seen; the exact time is deliberately not kept.
/// </summary>
public sealed record QueuedExchange(string Make, string Model, string Year, string Protocol, CollectedExchange Exchange, string? ObservedOn = null)
{
    public string Key => $"{Make}|{Model}|{Year}|{Exchange.Key}";
}

/// <summary>
/// Small file-backed queue. It never holds more than <see cref="Capacity"/> entries, stores each distinct
/// observation only once, remembers which ones were already uploaded (so the same compatibility fact is not
/// re-sent in every session), survives a corrupt or missing file, and is wiped completely when the user
/// withdraws consent. A pending server-side deletion is remembered until it succeeds.
/// </summary>
public sealed class CollectionQueue
{
    public const int Capacity = 400;
    public const int SentCapacity = 5000;

    private readonly string _path;
    private readonly string _sentPath;
    private readonly string _deletePath;
    private readonly object _gate = new();
    private readonly Dictionary<string, QueuedExchange> _items = new();
    private readonly List<string> _order = new();
    private readonly HashSet<string> _sent = new();
    private readonly Queue<string> _sentOrder = new();

    public CollectionQueue(string path)
    {
        _path = path;
        _sentPath = path + ".sent";
        _deletePath = path + ".delete";
        Load();
    }

    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    public int SentCount
    {
        get { lock (_gate) return _sent.Count; }
    }

    /// <summary>True when consent was withdrawn but the server has not confirmed the deletion yet.</summary>
    public bool DeletePending
    {
        get { lock (_gate) return File.Exists(_deletePath); }
        set
        {
            lock (_gate)
            {
                try
                {
                    if (value)
                    {
                        var directory = Path.GetDirectoryName(_deletePath);
                        if (!string.IsNullOrEmpty(directory))
                            Directory.CreateDirectory(directory);
                        File.WriteAllText(_deletePath, "1");
                    }
                    else if (File.Exists(_deletePath))
                    {
                        File.Delete(_deletePath);
                    }
                }
                catch
                {
                    // best effort
                }
            }
        }
    }

    /// <summary>False when the same observation is already queued or was already uploaded.</summary>
    public bool Add(QueuedExchange item)
    {
        lock (_gate)
        {
            if (_items.ContainsKey(item.Key) || _sent.Contains(item.Key))
                return false;

            if (_items.Count >= Capacity)
            {
                var oldest = _order[0];
                _order.RemoveAt(0);
                _items.Remove(oldest);
            }

            _items[item.Key] = item;
            _order.Add(item.Key);
            Save();
            return true;
        }
    }

    public IReadOnlyList<QueuedExchange> Peek(int count)
    {
        lock (_gate)
            return _order.Take(count).Select(k => _items[k]).ToArray();
    }

    /// <summary>Removes uploaded items and remembers them as sent.</summary>
    public void Remove(IEnumerable<QueuedExchange> sent)
    {
        lock (_gate)
        {
            foreach (var item in sent)
            {
                if (_items.Remove(item.Key))
                    _order.Remove(item.Key);
                if (_sent.Add(item.Key))
                {
                    _sentOrder.Enqueue(item.Key);
                    while (_sentOrder.Count > SentCapacity)
                        _sent.Remove(_sentOrder.Dequeue());
                }
            }

            Save();
            SaveSent();
        }
    }

    /// <summary>Forgets everything: queued observations and the already-sent memory.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            _order.Clear();
            _sent.Clear();
            _sentOrder.Clear();
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
            try { if (File.Exists(_sentPath)) File.Delete(_sentPath); } catch { }
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<List<QueuedExchange>>(File.ReadAllText(_path));
                foreach (var item in loaded ?? new())
                {
                    if (item?.Exchange is null || _items.ContainsKey(item.Key))
                        continue;
                    _items[item.Key] = item;
                    _order.Add(item.Key);
                }
            }
        }
        catch
        {
            _items.Clear();
            _order.Clear(); // a damaged queue file is simply discarded
        }

        try
        {
            if (File.Exists(_sentPath))
            {
                foreach (var key in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_sentPath)) ?? new())
                {
                    if (!string.IsNullOrEmpty(key) && _sent.Add(key))
                        _sentOrder.Enqueue(key);
                }
            }
        }
        catch
        {
            _sent.Clear();
            _sentOrder.Clear();
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(_order.Select(k => _items[k]).ToList()));
        }
        catch
        {
            // Storage problems must never affect diagnostics; the entry just stays in memory.
        }
    }

    private void SaveSent()
    {
        try
        {
            var directory = Path.GetDirectoryName(_sentPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_sentPath, JsonSerializer.Serialize(_sentOrder.ToList()));
        }
        catch
        {
        }
    }
}

/// <summary>
/// The only way an observed exchange gets into the queue: vehicle description present, then the matching
/// sanitiser. OEM module exchanges are sanitised raw; standard OBD2 exchanges become compatibility summaries.
/// The two kinds are never routed through each other's rules.
/// </summary>
public static class CollectionIntake
{
    public static bool TryAdd(CollectionQueue queue, ObdRawExchange exchange,
        string? make, string? model, string? year, string? protocol, DateTimeOffset? now = null)
    {
        var item = TryCreate(exchange, make, model, year, protocol, now);
        return item is not null && queue.Add(item);
    }

    /// <summary>The sanitised record for an observed exchange, or null when nothing may be kept.</summary>
    public static QueuedExchange? TryCreate(ObdRawExchange exchange,
        string? make, string? model, string? year, string? protocol, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(make) || string.IsNullOrWhiteSpace(model))
            return null; // without a vehicle description the sample would be useless

        // A failed decode can leave the VIN itself in a text field: such a sample is dropped, not trimmed.
        if (CollectionSanitizer.LooksLikeVin(make) || CollectionSanitizer.LooksLikeVin(model) ||
            CollectionSanitizer.LooksLikeVin(year) || CollectionSanitizer.LooksLikeVin(protocol))
            return null;

        var clean = exchange?.Origin == ObdExchangeOrigin.StandardObd
            ? StandardObdClassifier.Summarize(exchange)
            : CollectionSanitizer.Sanitize(exchange!);
        if (clean is null)
            return null;

        static string Cut(string? value, int max)
        {
            var text = (value ?? string.Empty).Trim();
            return text.Length > max ? text[..max] : text;
        }

        var day = (now ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyy-MM-dd");
        return new QueuedExchange(Cut(make, 40), Cut(model, 60), Cut(year, 8), Cut(protocol, 60), clean, day);
    }
}
