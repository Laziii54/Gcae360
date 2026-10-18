using CarScanner.Models;
using CarScanner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CarScanner.ViewModels;

public partial class SensorsViewModel : BaseViewModel
{
    private static readonly string[] CategoryOrder =
    [
        "Engine", "Fuel", "Air / Intake", "Temperature", "Oxygen Sensors",
        "Emissions", "Throttle", "Vehicle Speed", "Electrical", "Status", "Other (raw data)"
    ];

    private readonly OBDService _obd;
    private readonly UnitSystemService _units;
    private IReadOnlyList<SensorValue> _rawSensors = Array.Empty<SensorValue>();

    // Kept as the complete raw result for code that needs a flat list; the UI
    // presents the same values through SensorGroups below.
    public ObservableCollection<SensorValue> Sensors { get; } = new();
    public ObservableCollection<SensorGroup> SensorGroups { get; } = new();

    [ObservableProperty] private string status = "განახლებისთვის დააჭირე ღილაკს.";
    [ObservableProperty] private string scanProgressText = "";
    [ObservableProperty] private double scanProgress;
    [ObservableProperty] private string emptySensorsMessage = "ჯერ არ არის წაკითხული სენსორები.";

    public SensorsViewModel(OBDService obd, UnitSystemService units)
    {
        _obd = obd;
        _units = units;
        Title = "ყველა სენსორი";
        Attach();
    }

    // Detached by the page when it goes away (no leak through singleton services).
    private bool _attached;
    private bool _refreshOnAttach;

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _units.UnitSystemChanged += HandleUnitSystemChanged;

        // Texts may have changed while the page was hidden (e.g. Settings on top).
        if (_refreshOnAttach)
        {
            _refreshOnAttach = false;
            HandleUnitSystemChanged(this, EventArgs.Empty);
        }
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _units.UnitSystemChanged -= HandleUnitSystemChanged;
        _refreshOnAttach = true;
    }

    private void HandleUnitSystemChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshUnitPresentation);

    [RelayCommand]
    public async Task Refresh()
    {
        try
        {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ScanProgress = 0;
            ScanProgressText = "Supported PID-ების შემოწმება…";
            Status = "ECU-ის მხარდაჭერილი სტანდარტული სენსორები იკითხება…";

            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();

            var progress = new Progress<SensorScanProgress>(item =>
            {
                ScanProgress = item.Total == 0 ? 0 : item.Completed / (double)item.Total;
                ScanProgressText = item.Total == 0
                    ? item.SensorName
                    : $"{item.Completed} / {item.Total} · {item.SensorName}";
            });

            var result = await _obd.ReadExpandedSupportedSensorValuesAsync(progress);
            _rawSensors = result.Values;
            var items = _rawSensors.Select(_units.Convert).ToArray();
            var stats = result.Stats;

            Sensors.Clear();
            foreach (var item in items)
                Sensors.Add(item);

            RebuildGroups(items);
            ScanProgress = 1;
            ScanProgressText = "სენსორების სკანირება დასრულდა.";

            Status = $"ECU-მ დაადასტურა {stats.Advertised} PID · რეალურად წაკითხულია {stats.Decoded} სენსორი · მიუწვდომელი პასუხი: {stats.Failed}.";
            EmptySensorsMessage = stats.Advertised == 0
                ? "ამ ECU-მ სტანდარტული, გასაშიფრი სენსორი არ დაადასტურა. Not Supported PID-ები დამალულია."
                : "დადასტურებული PID-ებიდან რეალური მნიშვნელობა ვერ წაიკითხა. მიუწვდომელი PID-ები დამალულია.";
        }
        catch (Exception ex)
        {
            Status = string.IsNullOrWhiteSpace(ex.Message)
                ? "სენსორების წაკითხვა ვერ დაიწყო. შეამოწმე OBD კავშირი."
                : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "SensorsViewModel.Refresh");
        }
    }

    // ---- live graph of one sensor (the sheet on the page) -------------------------------------------------------
    // The scan above is a one-time read, so the graph polls only the tapped PID, with the same single short
    // attempt the live dashboard uses, and only while the sheet is open.
    private CancellationTokenSource? _graphCts;

    /// <summary>Starts reading one sensor again and again; returns the value source the graph listens to (null when the value is not a number).</summary>
    public LiveSensorSource? StartGraph(SensorValue sensor)
    {
        if (!double.TryParse(sensor.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var first))
            return null;

        StopGraph();
        var source = new LiveSensorSource { Name = sensor.Name, Unit = sensor.Unit, Value = first };
        var cts = new CancellationTokenSource();
        _graphCts = cts;
        _ = PollGraphAsync(sensor.Pid, source, cts.Token);
        return source;
    }

    public void StopGraph()
    {
        var cts = _graphCts;
        _graphCts = null;
        if (cts is null)
            return;

        cts.Cancel();
        cts.Dispose();
    }

    private async Task PollGraphAsync(int pid, LiveSensorSource source, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                // A running scan owns the adapter: wait for it instead of competing.
                if (!IsBusy)
                {
                    var values = await _obd.ReadLiveValuesAsync(new[] { pid }, token);
                    if (values.Count > 0)
                    {
                        var converted = _units.Convert(values[0]);
                        if (double.TryParse(converted.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                            await MainThread.InvokeOnMainThreadAsync(() => source.Value = number);
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            CarScanner.Services.CommandErrors.Report(exception, "SensorsViewModel.PollGraphAsync");
        }
    }

    private void RebuildGroups(IReadOnlyList<SensorValue> items)
    {
        SensorGroups.Clear();

        foreach (var category in CategoryOrder)
        {
            var values = items
                .Where(item => item.Category == category)
                .OrderBy(item => item.Pid)
                .ToArray();

            if (values.Length == 0)
                continue;

            SensorGroups.Add(new SensorGroup
            {
                Name = category,
                Sensors = values
            });
        }
    }

    private void RefreshUnitPresentation()
    {
        var converted = _rawSensors.Select(_units.Convert).ToArray();
        Sensors.Clear();
        foreach (var item in converted)
            Sensors.Add(item);
        RebuildGroups(converted);
    }
}
