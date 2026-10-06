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
        "Emissions", "Throttle", "Vehicle Speed", "Electrical"
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
        _units.UnitSystemChanged += (_, __) => MainThread.BeginInvokeOnMainThread(RefreshUnitPresentation);
        Title = "ყველა სენსორი";
    }

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
