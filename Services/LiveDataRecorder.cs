using CarScanner.Models;
using System.Text.Json;

namespace CarScanner.Services;

public sealed class LiveDataRecorder : IDisposable
{
    private readonly object _gate = new();
    private readonly List<ObdTelemetrySample> _samples = new();
    private bool _recording;
    public const int MaxSamples = 30000;

    public bool IsRecording { get { lock (_gate) return _recording; } }
    public IReadOnlyList<ObdTelemetrySample> Samples { get { lock (_gate) return _samples.ToArray(); } }

    public void Start()
    {
        lock (_gate) { _samples.Clear(); _recording = true; }
    }

    public void Stop() { lock (_gate) _recording = false; }

    public void Add(IEnumerable<ObdTelemetrySample> samples)
    {
        lock (_gate)
        {
            if (!_recording) return;
            _samples.AddRange(samples);
            if (_samples.Count > MaxSamples)
                _samples.RemoveRange(0, _samples.Count - MaxSamples);
        }
    }

    public string ExportJson()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(Samples, options);
    }

    public string ExportCsv()
    {
        var sb = new System.Text.StringBuilder("Timestamp,PID,Name,Unit,Value\n");
        foreach (var s in Samples)
            sb.Append(s.Timestamp.ToString("O")).Append(',').Append(s.Pid.ToString("X2")).Append(',')
              .Append(Escape(s.Name)).Append(',').Append(Escape(s.Unit)).Append(',').Append(s.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        return sb.ToString();
    }

    private static string Escape(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    public void Dispose() { lock (_gate) _recording = false; }
}
