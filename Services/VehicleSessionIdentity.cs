using CarScanner.Models;

namespace CarScanner.Services;

/// <summary>
/// The identity of the vehicle that is connected RIGHT NOW. It lives only in memory for one connection session: a new session
/// starts empty, a disconnect invalidates it, and a late answer that belongs to an earlier session is refused. Saved vehicle
/// history (identity cache, database, reports) is a different thing and is never touched here.
/// </summary>
public sealed class VehicleSessionIdentity
{
    private readonly object _gate = new();
    private int _generation;
    private VehicleInfo? _current;

    /// <summary>Changes every time the session is invalidated; work started under an older number must not publish.</summary>
    public int Generation
    {
        get { lock (_gate) return _generation; }
    }

    /// <summary>The vehicle identified in this session, or null (not connected, not identified yet, or identification failed).</summary>
    public VehicleInfo? Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>Stores <paramref name="info"/> only when <paramref name="generation"/> is still the current session.</summary>
    public bool TrySet(int generation, VehicleInfo info)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return false;
            _current = info;
            return true;
        }
    }

    /// <summary>Ends the session: forgets the vehicle and makes every answer still in flight stale.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _generation++;
            _current = null;
        }
    }
}
