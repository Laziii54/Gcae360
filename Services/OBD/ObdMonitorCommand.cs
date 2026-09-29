namespace CarScanner.Services.OBD;

/// <summary>
/// Marks a command that puts the adapter into CAN monitoring ("ATMA") for a
/// fixed time. The transport sends the command, collects everything the
/// adapter prints during that time, then sends a carriage return (which stops
/// monitoring) and waits for the prompt. Used only for reading frames the car
/// broadcasts by itself (e.g. the odometer); nothing is sent to the car.
/// </summary>
public static class ObdMonitorCommand
{
    private const string Prefix = "MONITOR:";

    public static string Create(string command, int milliseconds) =>
        $"{Prefix}{Math.Clamp(milliseconds, 200, 10000)}:{command}";

    public static bool TryParse(string command, out int milliseconds, out string inner)
    {
        milliseconds = 0;
        inner = command;
        if (!command.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var rest = command[Prefix.Length..];
        var colon = rest.IndexOf(':');
        if (colon <= 0 || !int.TryParse(rest[..colon], out milliseconds))
            return false;

        inner = rest[(colon + 1)..];
        return inner.Length > 0;
    }
}
