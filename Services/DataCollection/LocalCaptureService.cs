using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Services.DataCollection;

/// <summary>
/// Platform adapter for <see cref="LocalCaptureSession"/>: a recording the user starts and stops, written to a
/// file the user shares on purpose. It only listens to <see cref="ObdExchangeTap"/>; it sends nothing to the car
/// and nothing to the network, and it is independent of the optional upload consent.
/// </summary>
public sealed class LocalCaptureService : IDisposable
{
    private readonly VehicleInfoService _vehicle;
    private readonly LocalCaptureSession _session = new();
    private OemCaptureTargetCatalog? _targets;
    private bool _subscribed;

    public LocalCaptureService(VehicleInfoService vehicle) => _vehicle = vehicle;

    public bool IsRecording => _session.IsRecording;

    public int ItemCount => _session.ItemCount;

    public async Task<IReadOnlyList<CaptureTarget>> GetTargetsAsync()
    {
        if (_targets is null)
        {
            try
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync("oem_capture_targets.json").ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                _targets = OemCaptureTargetCatalog.Parse(await reader.ReadToEndAsync().ConfigureAwait(false));
            }
            catch
            {
                _targets = new OemCaptureTargetCatalog(Array.Empty<CaptureTarget>());
            }
        }

        return _targets.Targets;
    }

    public void Start(string? targetId)
    {
        _session.Start(targetId, CurrentVehicle);
        if (!_subscribed)
        {
            ObdExchangeTap.Observed += _session.Observe;
            _subscribed = true;
        }
    }

    public void Stop()
    {
        _session.Stop();
        Unsubscribe();
    }

    /// <summary>Writes the recording to the cache folder and returns the file path, or null when nothing was recorded.</summary>
    public async Task<string?> SaveAsync()
    {
        if (_session.ItemCount == 0)
            return null;

        var json = _session.BuildJson(AppInfo.Current.VersionString);
        var path = Path.Combine(FileSystem.CacheDirectory, $"gcar360-capture-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        return path;
    }

    private CaptureVehicle? CurrentVehicle()
    {
        var v = _vehicle.CurrentVehicle;
        return v is null ? null : new CaptureVehicle(v.Manufacturer, v.Model, v.Year, v.Protocol);
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;
        ObdExchangeTap.Observed -= _session.Observe;
        _subscribed = false;
    }

    public void Dispose() => Unsubscribe();
}
