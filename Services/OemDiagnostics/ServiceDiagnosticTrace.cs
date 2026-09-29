using System.Text.Json;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>Bounded in-memory Service trace. Records existing traffic only; never sends commands.</summary>
public static class ServiceDiagnosticTrace
{
    private static readonly object Gate = new();
    private static readonly Queue<Entry> Entries = new();
    public sealed record Entry(DateTimeOffset TimestampUtc, string Context, string Request,
        string Response, string Result, int? TxCanId, int? RxCanId);

    public static void Record(string context, string request, string response, string result,
        int? tx = null, int? rx = null)
    {
        // Bound both entry count and individual raw responses. No VIN is recorded.
        static string Limit(string value) => value.Length <= 4096 ? value : value[..4096] + " [truncated]";
        lock (Gate)
        {
            while (Entries.Count >= 200) Entries.Dequeue();
            Entries.Enqueue(new(DateTimeOffset.UtcNow, Limit(context), Limit(request),
                Limit(response), Limit(result), tx, rx));
        }
    }

    public static string ExportJson()
    {
        lock (Gate)
            return JsonSerializer.Serialize(new { SchemaVersion = 1,
                CaptureKind = "Application Service traffic; not an external CAN sniffer",
                VinRedacted = true,
                Entries = SnapshotForExportLocked() }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Returns a privacy-safe copy of the current read-only trace. Requests
    /// that can return a VIN remain visible, but their responses are removed.
    /// </summary>
    public static IReadOnlyList<Entry> SnapshotForExport()
    {
        lock (Gate)
            return SnapshotForExportLocked();
    }

    private static Entry[] SnapshotForExportLocked() => Entries
        .Select(entry => IsVehicleIdentifierRequest(entry.Request)
            ? entry with { Response = "[redacted vehicle identifier]", Result = "Redacted" }
            : entry)
        .ToArray();

    private static bool IsVehicleIdentifierRequest(string request)
    {
        var normalized = new string((request ?? string.Empty)
            .Where(Uri.IsHexDigit)
            .ToArray())
            .ToUpperInvariant();

        return normalized.StartsWith("22F190", StringComparison.Ordinal) ||
               normalized.StartsWith("1A90", StringComparison.Ordinal) ||
               normalized.StartsWith("0902", StringComparison.Ordinal);
    }
}
