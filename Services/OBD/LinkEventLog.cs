using System.Text;

namespace CarScanner.Services.OBD;

/// <summary>
/// A short, in-memory diary of what happened to the adapter link (connect, loss and why, reconnect attempts, command
/// timeouts, recovery). Nothing is sent anywhere: it exists so that "the adapter stopped answering" can be explained
/// from facts — the user copies it from the Adapter Center. No VIN, no personal data; only times, events and commands.
/// </summary>
public static class LinkEventLog
{
    private const int Capacity = 400;
    private static readonly object Gate = new();
    private static readonly Queue<string> Lines = new();

    public static void Add(string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {text}";
        lock (Gate)
        {
            Lines.Enqueue(line);
            while (Lines.Count > Capacity)
                Lines.Dequeue();
        }
    }

    public static string Export()
    {
        lock (Gate)
        {
            var builder = new StringBuilder("OBD360 connection log (newest last)\n");
            foreach (var line in Lines)
                builder.AppendLine(line);
            return builder.ToString();
        }
    }

    public static void Clear()
    {
        lock (Gate)
            Lines.Clear();
    }
}
