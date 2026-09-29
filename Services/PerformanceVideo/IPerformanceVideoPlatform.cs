namespace CarScanner.Services.PerformanceVideo;

/// <summary>Camera preview + recording, implemented per platform by the camera view handler.</summary>
public interface IPerformanceCameraController
{
    bool IsRecording { get; }
    Task StartPreviewAsync();
    Task StartRecordingAsync(string outputPath, bool withAudio);
    Task<string?> StopRecordingAsync();
    void Release();
}

/// <summary>Burns the overlay into a video and saves files to the gallery.</summary>
public interface IPerformanceVideoExporter
{
    bool IsSupported { get; }

    Task ComposeWithOverlayAsync(
        string inputPath,
        string outputPath,
        PerformanceVideoTelemetry telemetry,
        PerformanceOverlayStyle style,
        IProgress<double>? progress,
        CancellationToken token);

    Task<bool> EnsureSavePermissionAsync();

    Task SaveToGalleryAsync(string filePath, string displayName, CancellationToken token);
}

/// <summary>Used on platforms without a Performance Video implementation.</summary>
public sealed class UnsupportedPerformanceVideoExporter : IPerformanceVideoExporter
{
    public bool IsSupported => false;

    public Task ComposeWithOverlayAsync(string inputPath, string outputPath, PerformanceVideoTelemetry telemetry, PerformanceOverlayStyle style, IProgress<double>? progress, CancellationToken token) =>
        throw new PlatformNotSupportedException();

    public Task<bool> EnsureSavePermissionAsync() => Task.FromResult(false);

    public Task SaveToGalleryAsync(string filePath, string displayName, CancellationToken token) =>
        throw new PlatformNotSupportedException();
}
