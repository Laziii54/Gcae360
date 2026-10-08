using System.Globalization;
using System.Text;
using System.Text.Json;
using CarScanner.Models;

namespace CarScanner.Services;

/// <summary>
/// Persists finished live-data recordings in the app's private storage.
/// It never manufactures telemetry: every stored row originated in an OBD PID
/// response collected by LiveDataRecorder.
/// </summary>
public sealed class RecordingSessionService
{
    private const string IndexFileName = "sessions.json";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private string RootDirectory => Path.Combine(FileSystem.AppDataDirectory, "live-recordings");
    private string IndexPath => Path.Combine(RootDirectory, IndexFileName);

    public async Task<IReadOnlyList<RecordingSession>> GetSessionsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var sessions = await ReadIndexLockedAsync();
            return sessions
                .OrderByDescending(session => session.StartedAt)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecordingSession?> SaveAsync(
        IReadOnlyList<ObdTelemetrySample> samples,
        string vehicleName,
        string vin,
        bool roundValues)
    {
        if (samples.Count == 0)
            return null;

        var copiedSamples = samples
            .Select(sample => new ObdTelemetrySample
            {
                Timestamp = sample.Timestamp,
                Pid = sample.Pid,
                Name = sample.Name,
                Unit = sample.Unit,
                Value = roundValues ? Math.Round(sample.Value, 2, MidpointRounding.AwayFromZero) : sample.Value
            })
            .ToList();

        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(RootDirectory);
            var id = Guid.NewGuid().ToString("N");
            var started = copiedSamples.Min(sample => sample.Timestamp);
            var ended = copiedSamples.Max(sample => sample.Timestamp);
            var dataFileName = $"recording-{started:yyyyMMdd-HHmmss}-{id[..8]}.json";
            var filePath = Path.Combine(RootDirectory, dataFileName);
            var initial = new RecordingSession
            {
                Id = id,
                StartedAt = started,
                EndedAt = ended,
                VehicleName = string.IsNullOrWhiteSpace(vehicleName) ? "მანქანა უცნობია" : vehicleName,
                Vin = vin?.Trim() ?? string.Empty,
                SampleCount = copiedSamples.Count,
                DataFileName = dataFileName
            };

            await WriteSessionFileAsync(filePath, initial, copiedSamples);
            var saved = new RecordingSession
            {
                Id = initial.Id,
                StartedAt = initial.StartedAt,
                EndedAt = initial.EndedAt,
                VehicleName = initial.VehicleName,
                Vin = initial.Vin,
                SampleCount = initial.SampleCount,
                DataFileName = initial.DataFileName,
                FileSizeBytes = new FileInfo(filePath).Length
            };

            // Store the final size in the data file too, so an exported archive
            // remains self-describing outside the app.
            await WriteSessionFileAsync(filePath, saved, copiedSamples);
            var index = await ReadIndexLockedAsync();
            index.RemoveAll(session => session.Id == saved.Id);
            index.Add(saved);
            await WriteIndexLockedAsync(index);
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecordingSession?> ImportAsync(FileResult file)
    {
        await using var stream = await file.OpenReadAsync();
        var imported = await TryReadImportedDataAsync(stream);
        if (imported.Count == 0)
            return null;

        var vehicleName = "Imported Live Data";
        var vin = string.Empty;
        return await SaveAsync(imported, vehicleName, vin, roundValues: false);
    }

    public async Task<string?> CreateCsvExportAsync(RecordingSession session)
    {
        var data = await ReadDataAsync(session);
        if (data is null)
            return null;

        var path = Path.Combine(FileSystem.CacheDirectory, $"live-data-{session.StartedAt:yyyyMMdd-HHmmss}.csv");
        await File.WriteAllTextAsync(path, ToCsv(data.Samples), Encoding.UTF8);
        return path;
    }

    public async Task<string?> CreateJsonExportAsync(RecordingSession session)
    {
        var data = await ReadDataAsync(session);
        if (data is null)
            return null;

        var path = Path.Combine(FileSystem.CacheDirectory, $"live-data-{session.StartedAt:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, _json), Encoding.UTF8);
        return path;
    }

    public async Task<bool> DeleteAsync(RecordingSession session)
    {
        await _gate.WaitAsync();
        try
        {
            var index = await ReadIndexLockedAsync();
            if (index.RemoveAll(item => item.Id == session.Id) > 0)
                await WriteIndexLockedAsync(index);

            var dataPath = Path.Combine(RootDirectory, session.DataFileName);
            if (File.Exists(dataPath))
                File.Delete(dataPath);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads the samples of one saved recording (used by the vehicle report).</summary>
    public Task<RecordingSessionData?> LoadAsync(RecordingSession session) => ReadDataAsync(session);

    private async Task<RecordingSessionData?> ReadDataAsync(RecordingSession session)
    {
        await _gate.WaitAsync();
        try
        {
            var path = Path.Combine(RootDirectory, session.DataFileName);
            if (!File.Exists(path))
                return null;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<RecordingSessionData>(stream, _json);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<RecordingSession>> ReadIndexLockedAsync()
    {
        if (!File.Exists(IndexPath))
            return new List<RecordingSession>();

        try
        {
            await using var stream = File.OpenRead(IndexPath);
            return await JsonSerializer.DeserializeAsync<List<RecordingSession>>(stream, _json) ?? new List<RecordingSession>();
        }
        catch (JsonException)
        {
            // A damaged index must not make live data unusable. Existing data
            // files are kept untouched and the next save creates a fresh index.
            return new List<RecordingSession>();
        }
    }

    private async Task WriteIndexLockedAsync(List<RecordingSession> sessions)
    {
        Directory.CreateDirectory(RootDirectory);
        var temporaryPath = IndexPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(sessions, _json), Encoding.UTF8);
        File.Move(temporaryPath, IndexPath, overwrite: true);
    }

    private async Task WriteSessionFileAsync(string path, RecordingSession session, List<ObdTelemetrySample> samples)
    {
        var data = new RecordingSessionData { Session = session, Samples = samples };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, _json), Encoding.UTF8);
    }

    private async Task<List<ObdTelemetrySample>> TryReadImportedDataAsync(Stream stream)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var raw = memory.ToArray();

        try
        {
            var wrapped = JsonSerializer.Deserialize<RecordingSessionData>(raw, _json);
            if (wrapped?.Samples.Count > 0)
                return wrapped.Samples;
        }
        catch (JsonException)
        {
            // Old exporter wrote only the sample array; try that safe format.
        }

        try
        {
            return JsonSerializer.Deserialize<List<ObdTelemetrySample>>(raw, _json) ?? new List<ObdTelemetrySample>();
        }
        catch (JsonException)
        {
            return new List<ObdTelemetrySample>();
        }
    }

    private static string ToCsv(IEnumerable<ObdTelemetrySample> samples)
    {
        var builder = new StringBuilder("Timestamp,PID,Name,Unit,Value\n");
        foreach (var sample in samples)
        {
            builder.Append(sample.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.Pid.ToString("X2", CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(sample.Name)).Append(',')
                .Append(Escape(sample.Unit)).Append(',')
                .Append(sample.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        return builder.ToString();
    }

    private static string Escape(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
}
