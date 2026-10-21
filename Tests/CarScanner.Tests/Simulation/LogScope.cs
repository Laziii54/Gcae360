using CarScanner.Services.OBD;

namespace CarScanner.Tests.Simulation;

/// <summary>
/// The lines the connection log received from the code a test runs (and from nothing else), although the log is shared by
/// every test running in parallel. Create it before the code under test starts.
/// </summary>
public sealed class LogScope : IDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

    public LogScope() => LinkEventLog.Listener.Value = _lines.Enqueue;

    public IReadOnlyList<string> Lines => _lines.ToArray();

    public string Text => string.Join('\n', _lines);

    /// <summary>The first line that contains <paramref name="fragment"/>, or null.</summary>
    public string? Find(string fragment) => _lines.FirstOrDefault(line => line.Contains(fragment, StringComparison.Ordinal));

    /// <summary>The index of the first line that contains each fragment, in the order given; fails if one is missing or out of order.</summary>
    public void AssertInOrder(params string[] fragments)
    {
        var lines = Lines;
        var from = 0;
        foreach (var fragment in fragments)
        {
            var at = -1;
            for (var i = from; i < lines.Count; i++)
            {
                if (lines[i].Contains(fragment, StringComparison.Ordinal))
                {
                    at = i;
                    break;
                }
            }

            if (at < 0)
                throw new Xunit.Sdk.XunitException($"Expected a log line containing \"{fragment}\" after line {from}.\nLog:\n{Text}");
            from = at + 1;
        }
    }

    public void Dispose() => LinkEventLog.Listener.Value = null;
}
