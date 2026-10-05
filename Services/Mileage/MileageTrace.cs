namespace CarScanner.Services.Mileage;

/// <summary>
/// Collects what the mileage check asked the car and what it answered, so a
/// failed check can be explained on the page and copied for support.
/// </summary>
public static class MileageTrace
{
    private static readonly object Gate = new();
    private static readonly List<string> Lines = new();

    public static void Reset()
    {
        lock (Gate)
            Lines.Clear();
    }

    public static void Add(string line)
    {
        lock (Gate)
        {
            if (Lines.Count < 200)
                Lines.Add(line);
        }
    }

    public static string Snapshot()
    {
        lock (Gate)
            return string.Join(Environment.NewLine, Lines);
    }
}
