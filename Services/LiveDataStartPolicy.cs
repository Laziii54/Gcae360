namespace CarScanner.Services;

/// <summary>
/// The OBD adapter link is not up (or went away). This is an expected state, not a defect: callers show a "connect the
/// adapter" message instead of reporting a crash. It derives from <see cref="InvalidOperationException"/> so existing
/// catch sites keep working, and keeps the message the service always used.
/// </summary>
public sealed class AdapterNotConnectedException : InvalidOperationException
{
    public AdapterNotConnectedException(string message) : base(message)
    {
    }
}

public enum LiveDataStartDecision
{
    /// <summary>Polling already runs: nothing to start.</summary>
    AlreadyRunning,

    /// <summary>No adapter link and no initialised adapter: starting would only throw.</summary>
    AdapterNotConnected,

    /// <summary>Go ahead (initialise the adapter first when it is not initialised yet).</summary>
    Proceed
}

public enum LiveDataStartResult
{
    Started,
    AlreadyRunning,
    AdapterNotConnected,
    Cancelled,
    Failed
}

/// <summary>The start rules of live-data polling, separate from the engine so they can be tested.</summary>
public static class LiveDataStartPolicy
{
    public static LiveDataStartDecision Decide(bool running, bool adapterInitialized, bool adapterConnected)
    {
        if (running)
            return LiveDataStartDecision.AlreadyRunning;
        return adapterInitialized || adapterConnected ? LiveDataStartDecision.Proceed : LiveDataStartDecision.AdapterNotConnected;
    }
}
