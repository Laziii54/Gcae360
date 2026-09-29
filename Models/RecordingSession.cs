namespace CarScanner.Models;

/// <summary>
/// Metadata for one locally saved, real OBD live-data recording. Values are
/// copied from the ECU responses captured by <see cref="ObdTelemetrySample"/>.
/// </summary>
public sealed class RecordingSession
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAt { get; init; }
    public DateTime EndedAt { get; init; }
    public string VehicleName { get; init; } = "მანქანა უცნობია";
    public string Vin { get; init; } = string.Empty;
    public int SampleCount { get; init; }
    public long FileSizeBytes { get; init; }
    public string DataFileName { get; init; } = string.Empty;

    public TimeSpan Duration => EndedAt > StartedAt ? EndedAt - StartedAt : TimeSpan.Zero;
    public string DateText => StartedAt == default ? "თარიღი მიუწვდომელია" : StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string DurationText => Duration.TotalHours >= 1
        ? $"{Duration:hh\\:mm\\:ss}"
        : $"{Duration:mm\\:ss}";
    public string VehicleText => string.IsNullOrWhiteSpace(Vin) ? VehicleName : $"{VehicleName} · VIN {Vin}";
    public string FileSizeText => FileSizeBytes switch
    {
        < 1024 => $"{FileSizeBytes} B",
        < 1024 * 1024 => $"{FileSizeBytes / 1024d:0.0} KB",
        _ => $"{FileSizeBytes / (1024d * 1024d):0.0} MB"
    };
}

public sealed class RecordingSessionData
{
    public RecordingSession Session { get; init; } = new();
    public List<ObdTelemetrySample> Samples { get; init; } = new();
}
