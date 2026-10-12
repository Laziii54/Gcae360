using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace CarScanner.ViewModels;

public partial class LiveDataViewModel : BaseViewModel
{
    private const string LayoutPreferencePrefix = "live_data_layout_v1_";
    private const string GlobalFavoritesPreferenceKey = "live_data_favorites_v1";
    private readonly LiveDataEngine _engine;
    private readonly LiveDataRecorder _recorder;
    private readonly RecordingSessionService _recordingSessions;
    private readonly OBDService _obd;
    private readonly IBluetoothService _bluetooth;
    private readonly VehicleInfoService _vehicle;
    private readonly VehicleBrandProfileService _brand;
    private readonly UnitSystemService _units;
    private readonly OemLiveDataService _oemLiveData;
    private readonly List<ObdTelemetrySample> _liveGraph = new();
    private string? _layoutAppliedVin;
    private readonly Dictionary<string, int> _oemTelemetryIds = new(StringComparer.OrdinalIgnoreCase);
    private int _nextOemTelemetryId = 0x10000;

    private int _uiUpdatePending;
    private int _valuesUiUpdatePending;

    [ObservableProperty]
    private LiveDataModel data = new();

    [ObservableProperty]
    private bool isRecording;

    [ObservableProperty] private bool isPaused = true;
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private string connectionText = "გათიშულია";
    [ObservableProperty] private string vehicleName = "მანქანა არჩეული არ არის";
    [ObservableProperty] private string vin = "VIN მიუწვდომელია";
    [ObservableProperty] private string ecuName = "Engine / ECM (Generic OBD-II)";
    [ObservableProperty] private string protocol = "ჯერ არ არის განსაზღვრული";
    [ObservableProperty] private string adapterName = "OBD-II adapter";
    [ObservableProperty] private string connectionQuality = "მონაცემები ჯერ არ მოსულა";
    [ObservableProperty] private string dataRateText = "0 PID/s";
    [ObservableProperty] private string voltageText = "— V";
    [ObservableProperty] private string speedText = "—";
    [ObservableProperty] private string speedUnit = "km/h";
    [ObservableProperty] private string coolantText = "—";
    [ObservableProperty] private string voltageCardText = "— V";
    [ObservableProperty] private string selectedSource = "ყველა ხელმისაწვდომი OBD-II მონაცემი";
    [ObservableProperty] private string selectedCategory = "ყველა კატეგორია";
    [ObservableProperty] private string sourceMessage = "ECU-ის მიერ მხარდაჭერილი პარამეტრები გამოჩნდება ავტომატურად.";
    [ObservableProperty] private IReadOnlyList<int> graphPidIds = new[] { 0x0C, 0x0D, 0x05 };
    [ObservableProperty] private string lastSnapshotText = "Snapshot ჯერ არ შექმნილა";
    [ObservableProperty] private string lastRecordingText = "ჩაწერილი სესიები ჯერ არ არის.";
    [ObservableProperty] private bool roundRecordingValues;
    [ObservableProperty] private LivePidItem? selectedPid;
    [ObservableProperty] private bool hasSelectedPid;
    [ObservableProperty] private string selectedPidStatus = "აირჩიე მხარდაჭერილი PID.";
    [ObservableProperty] private string selectedPidStatusColor = "#94A3B8";

    // A graph refresh is a single snapshot assignment.  Rebuilding an
    // ObservableCollection one item at a time caused hundreds of redraws on
    // every live-data tick and made the UI feel frozen on Android.
    [ObservableProperty] private IReadOnlyList<ObdTelemetrySample> graphSamples = Array.Empty<ObdTelemetrySample>();
    public ObservableCollection<LivePidItem> Pids { get; } = new();
    public ObservableCollection<LivePidItem> VisiblePids { get; } = new();
    public ObservableCollection<LivePidItem> DashboardPids { get; } = new();
    public ObservableCollection<string> DataSources { get; } = new()
    {
        "ყველა ხელმისაწვდომი OBD-II მონაცემი", "Engine / ECM"
    };
    public IReadOnlyList<string> Categories { get; } = new[]
    {
        "ყველა კატეგორია", "ძრავა", "ჰაერი", "საწვავი", "ემისიები", "ელექტრო", "ჰიბრიდი", "დიაგნოსტიკა", "მანქანა"
    };

    public LiveDataViewModel(
        LiveDataEngine engine,
        LiveDataRecorder recorder,
        RecordingSessionService recordingSessions,
        OBDService obd,
        IBluetoothService bluetooth,
        VehicleInfoService vehicle,
        VehicleBrandProfileService brand,
        UnitSystemService units,
        OemLiveDataService oemLiveData)
    {
        _engine = engine;
        _recorder = recorder;
        _recordingSessions = recordingSessions;
        _obd = obd;
        _bluetooth = bluetooth;
        _vehicle = vehicle;
        _brand = brand;
        _units = units;
        _oemLiveData = oemLiveData;
        RoundRecordingValues = Preferences.Default.Get("round_live_recording_values", false);

        Title = "Live მონაცემები";

        _engine.LiveDataUpdated += OnLiveDataUpdated;
        _engine.LiveValuesUpdated += OnLiveValuesUpdated;
        _bluetooth.ConnectionChanged += OnConnectionChanged;
        _vehicle.VehicleInfoUpdated += OnVehicleInfoUpdated;
        _brand.Changed += (_, __) => MainThread.BeginInvokeOnMainThread(UpdateHeader);
        _units.UnitSystemChanged += OnUnitSystemChanged;
        _oemLiveData.ValuesUpdated += OnOemLiveValuesUpdated;
        UpdateHeader();
        RefreshUnitPresentation();
    }

    private void OnLiveDataUpdated(
        object? sender,
        LiveDataModel value)
    {
        // თუ UI-ს უკვე აქვს განახლება რიგში,
        // ახალი update აღარ დავამატოთ.
        if (Interlocked.Exchange(ref _uiUpdatePending, 1) == 1)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                Data = value;
                RefreshLiveCards(value);
            }
            finally
            {
                Interlocked.Exchange(ref _uiUpdatePending, 0);
            }
        });
    }

    private void OnLiveValuesUpdated(object? sender, IReadOnlyList<SensorValue> values)
    {
        if (Interlocked.Exchange(ref _valuesUiUpdatePending, 1) == 1)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                EnsurePidCatalog();
                var now = DateTime.Now;

                foreach (var value in values)
                {
                    var item = Pids.FirstOrDefault(x => !x.IsOem && x.Pid == value.Pid);
                    item?.Update(value, now, _units);
                }

                if (SelectedPid is not null && values.Any(value => value.Pid == SelectedPid.Pid))
                {
                    SelectedPidStatus = $"რეალურ დროში ახლდება · {now:HH:mm:ss}";
                    SelectedPidStatusColor = "#4ADE80";
                }

                var samples = values.Select(value => new ObdTelemetrySample
                {
                    Timestamp = now,
                    Pid = value.Pid,
                    Name = value.Name,
                    Unit = value.Unit,
                    Value = double.TryParse(value.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : 0
                }).ToArray();

                if (IsRecording)
                    _recorder.Add(samples);

                foreach (var sample in samples.Where(sample => GraphPidIds.Contains(sample.Pid)))
                    _liveGraph.Add(sample);
                while (_liveGraph.Count > 540)
                    _liveGraph.RemoveAt(0);

                RefreshGraphPresentation();

                DataRateText = $"{_engine.DataRate:0.0} PID/s";
                var voltage = values.FirstOrDefault(value => value.Pid == 0x42);
                if (voltage is not null && double.TryParse(voltage.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var voltageValue))
                    VoltageText = _units.Format(voltageValue, "V");

                ConnectionQuality = _engine.DataRate switch
                {
                    >= 8 => "ძალიან კარგი — სწრაფი პასუხი",
                    >= 3 => "სტაბილური — ECU პასუხობს",
                    > 0 => "ნელი პასუხი — შეამოწმე adapter/კონტაქტი",
                    _ => "ECU პასუხს ჯერ არ აბრუნებს"
                };
                StatusMessage = values.Count == 0
                    ? "არჩეულმა ECU-მ ამ ეტაპზე მონაცემი არ დააბრუნა."
                    : "Live მონაცემები ახლდება…";
            }
            finally
            {
                Interlocked.Exchange(ref _valuesUiUpdatePending, 0);
            }
        });
    }

    private void OnConnectionChanged(object? sender, bool connected) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsPaused = !connected;
            if (!connected && SelectedPid is not null)
            {
                _engine.SetFocusedPid(null);
                SelectedPidStatus = "Not Supported — OBD კავშირი გათიშულია.";
                SelectedPidStatusColor = "#F59E0B";
            }
            UpdateHeader();
        });

    private void OnUnitSystemChanged(object? sender, EventArgs eventArgs) =>
        MainThread.BeginInvokeOnMainThread(RefreshUnitPresentation);

    private void OnOemLiveValuesUpdated(object? sender, OemLiveDataReadResult result) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var now = result.CapturedAt.LocalDateTime;
            var values = result.Commands.SelectMany(command => command.Values).ToArray();
            foreach (var value in values)
            {
                var source = $"OEM · {value.EcuName}";
                if (!DataSources.Contains(source))
                    DataSources.Add(source);

                var key = $"{value.DefinitionId}|{value.DataIdentifier:X4}|{value.FieldKey}";
                if (!_oemTelemetryIds.TryGetValue(key, out var telemetryId))
                {
                    telemetryId = _nextOemTelemetryId++;
                    _oemTelemetryIds[key] = telemetryId;
                }

                var item = Pids.FirstOrDefault(pid => pid.IsOem &&
                    pid.DefinitionId.Equals(value.DefinitionId, StringComparison.OrdinalIgnoreCase) &&
                    pid.FieldKey.Equals(value.FieldKey, StringComparison.OrdinalIgnoreCase) &&
                    pid.IdentifierText.Equals(value.IdentifierText, StringComparison.OrdinalIgnoreCase));
                if (item is null)
                {
                    item = new LivePidItem
                    {
                        Pid = telemetryId,
                        IsOem = true,
                        DefinitionId = value.DefinitionId,
                        FieldKey = value.FieldKey,
                        IdentifierText = value.IdentifierText,
                        Name = value.Label,
                        RawUnit = value.Unit,
                        Category = value.Category,
                        Source = source
                    };
                    item.IsFavorite = LoadGlobalFavoriteKeys().Contains(FavoriteKey(item));
                    item.RefreshPresentation(_units);
                    Pids.Add(item);
                    if (SelectedSource.StartsWith("ყველა ხელმისაწვდომი", StringComparison.Ordinal))
                        SelectedSource = source;
                }
                item.UpdateOem(value.Value, now, _units);
            }

            var samples = values.Select(value =>
            {
                var key = $"{value.DefinitionId}|{value.DataIdentifier:X4}|{value.FieldKey}";
                return new ObdTelemetrySample
                {
                    Timestamp = now,
                    Pid = _oemTelemetryIds[key],
                    Name = value.Label,
                    Unit = value.Unit,
                    Value = value.Value
                };
            }).ToArray();
            if (IsRecording)
                _recorder.Add(samples);
            foreach (var sample in samples.Where(sample => GraphPidIds.Contains(sample.Pid)))
                _liveGraph.Add(sample);
            while (_liveGraph.Count > 540)
                _liveGraph.RemoveAt(0);

            DataRateText = $"{result.DataRate:0.0} DID values/s";
            SourceMessage = result.Detail;
            StatusMessage = result.Status == "AVAILABLE" || result.Status == "PARTIAL"
                ? "OEM Live Data რეალური ECU პასუხებით ახლდება."
                : result.Detail;
            RefreshGraphPresentation();
            RebuildVisiblePids();
        });

    private void RefreshUnitPresentation()
    {
        foreach (var item in Pids)
            item.RefreshPresentation(_units);

        RefreshLiveCards(Data);
        RefreshGraphPresentation();
    }

    private void RefreshLiveCards(LiveDataModel value)
    {
        SpeedText = value.HasSpeed ? _units.Convert(value.Speed, "km/h").Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "—";
        SpeedUnit = _units.SpeedUnit;
        CoolantText = value.HasCoolantTemp ? _units.FormatTemperature(value.CoolantTemp) : "—";
        VoltageCardText = value.HasVoltage ? _units.Format(value.Voltage, "V", "0.00") : "— V";
    }

    private void RefreshGraphPresentation() => GraphSamples = _liveGraph.Select(_units.Convert).ToArray();

    private void OnVehicleInfoUpdated(object? sender, VehicleInfo vehicle) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateHeader();
            ApplySavedLayoutForVehicle();
        });

    private void UpdateHeader()
    {
        IsConnected = _bluetooth.IsConnected;
        ConnectionText = IsConnected ? "ECU დაკავშირებულია" : "გათიშულია";
        AdapterName = string.IsNullOrWhiteSpace(_bluetooth.ConnectedDeviceName)
            ? "OBD-II adapter"
            : _bluetooth.ConnectedDeviceName;
        Protocol = _obd.IsInitialized ? _obd.CurrentProtocolDescription : "OBD პროტოკოლი იტვირთება…";

        var vehicle = _vehicle.CurrentVehicle;
        VehicleName = vehicle is null || string.IsNullOrWhiteSpace(vehicle.Manufacturer) || vehicle.Manufacturer == "Unknown"
            ? _brand.Current.DisplayName
            : string.Join(" ", new[] { vehicle.Manufacturer, vehicle.Model, vehicle.Year }
                .Where(value => !string.IsNullOrWhiteSpace(value) && value != "Unknown"));
        Vin = vehicle?.VIN is { Length: 17 } knownVin ? knownVin : "VIN ECU-დან ჯერ არ მოსულა";
        EcuName = vehicle?.ECUName is { Length: > 0 } ecu && ecu != "Unknown"
            ? ecu
            : "Engine / ECM (Generic OBD-II)";
    }

    private void EnsurePidCatalog()
    {
        var supported = _obd.SupportedPids.OrderBy(item => item.Pid).ToArray();
        var currentGeneric = Pids.Where(item => !item.IsOem).ToArray();
        if (supported.Length == 0 ||
            supported.Select(item => item.Pid).SequenceEqual(currentGeneric.Select(item => item.Pid)))
            return;

        var previous = Pids.ToDictionary(item => item.Pid);
        var oemItems = Pids.Where(item => item.IsOem).ToArray();
        var savedLayout = LoadLayoutForCurrentVehicle();
        var globalFavorites = LoadGlobalFavoriteKeys();
        Pids.Clear();
        foreach (var definition in supported)
        {
            previous.TryGetValue(definition.Pid, out var old);
            var item = new LivePidItem
            {
                Pid = definition.Pid,
                Name = definition.Name,
                RawUnit = definition.Unit,
                Category = definition.Category,
                IsFavorite = old?.IsFavorite ?? globalFavorites.Contains($"pid:{definition.Pid:X}"),
                IsDashboard = old?.IsDashboard ?? savedLayout?.DashboardPids.Contains(definition.Pid) ?? IsCorePid(definition.Pid),
                IsGraph = old?.IsGraph ?? savedLayout?.GraphPids.Contains(definition.Pid) ?? new[] { 0x0C, 0x0D, 0x05 }.Contains(definition.Pid)
            };
            item.RefreshPresentation(_units);
            Pids.Add(item);
        }
        foreach (var item in oemItems)
            Pids.Add(item);

        SyncDashboard();
        GraphPidIds = Pids.Where(pid => pid.IsGraph).Select(pid => pid.Pid).Take(3).ToArray();
        RebuildVisiblePids();
        MarkLayoutAppliedForCurrentVehicle();
    }

    partial void OnSelectedSourceChanged(string value) => RebuildVisiblePids();
    partial void OnSelectedCategoryChanged(string value) => RebuildVisiblePids();

    private void RebuildVisiblePids()
    {
        VisiblePids.Clear();
        var query = Pids.AsEnumerable();
        var sourceUsesGenericData = true;

        if (SelectedSource.StartsWith("OEM · ", StringComparison.Ordinal))
        {
            query = query.Where(item => item.IsOem && item.Source == SelectedSource);
            sourceUsesGenericData = false;
        }
        else if (SelectedSource == "Engine / ECM")
            query = query.Where(item => !item.IsOem && item.Category is "ძრავა" or "ჰაერი" or "ელექტრო");
        else if (SelectedSource == "Fuel System")
            query = query.Where(item => !item.IsOem && item.Category == "საწვავი");
        else if (SelectedSource == "Oxygen / Lambda")
            query = query.Where(item => !item.IsOem && item.Category == "ემისიები");
        else if (SelectedSource == "Hybrid / EV")
            query = query.Where(item => !item.IsOem && item.Category == "ჰიბრიდი");
        else if (SelectedSource is not "ყველა ხელმისაწვდომი OBD-II მონაცემი")
            sourceUsesGenericData = false;
        else
            query = query.Where(item => !item.IsOem);

        if (SelectedCategory != "ყველა კატეგორია")
            query = query.Where(item => item.Category == SelectedCategory);

        foreach (var item in query
                     .OrderByDescending(item => item.IsFavorite)
                     .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            VisiblePids.Add(item);

        SourceMessage = sourceUsesGenericData
            ? VisiblePids.Count == 0
                ? "ამ ECU-მ ამ კატეგორიის სტანდარტული PID ჯერ არ დაადასტურა."
                : "ნაჩვენებია მხოლოდ ECU-ის მიერ რეალურად მხარდაჭერილი PID-ები."
            : VisiblePids.Count > 0
                ? $"{SelectedSource}: ნაჩვენებია მხოლოდ რეალურად დაბრუნებული reviewed DID მნიშვნელობები."
                : $"{SelectedSource}: რეალური OEM მნიშვნელობა ჯერ არ დაბრუნებულა.";
    }

    [RelayCommand]
    private void ToggleDashboard(LivePidItem? item)
    {
        if (item is null) return;
        if (!item.IsDashboard && !IsCorePid(item.Pid) && Pids.Count(pid => pid.IsDashboard && !IsCorePid(pid.Pid)) >= 3)
        {
            StatusMessage = "სწრაფ დაფაზე ერთდროულად მაქსიმუმ 3 დამატებითი PID აირჩიე.";
            return;
        }

        item.IsDashboard = !item.IsDashboard;
        SyncDashboard();
        SaveLayoutForCurrentVehicle();
    }

    [RelayCommand]
    private void ToggleGraph(LivePidItem? item)
    {
        if (item is null) return;
        if (!item.IsGraph && Pids.Count(pid => pid.IsGraph) >= 3)
        {
            StatusMessage = "ერთ გრაფიკზე მაქსიმუმ 3 PID შეიძლება.";
            return;
        }

        item.IsGraph = !item.IsGraph;
        GraphPidIds = Pids.Where(pid => pid.IsGraph).Select(pid => pid.Pid).ToArray();
        _liveGraph.Clear();
        GraphSamples = Array.Empty<ObdTelemetrySample>();
        SaveLayoutForCurrentVehicle();
    }

    [RelayCommand]
    private void ToggleFavorite(LivePidItem? item)
    {
        if (item is not null)
        {
            item.IsFavorite = !item.IsFavorite;
            SaveGlobalFavorites();
            RebuildVisiblePids();
        }
    }

    private void SaveGlobalFavorites()
    {
        var keys = Pids.Where(item => item.IsFavorite).Select(FavoriteKey).Distinct().ToArray();
        Preferences.Default.Set(GlobalFavoritesPreferenceKey, JsonSerializer.Serialize(keys));
    }

    private static string FavoriteKey(LivePidItem item) => item.IsOem
        ? $"oem:{item.DefinitionId}:{item.FieldKey}:{item.IdentifierText}"
        : $"pid:{item.Pid:X}";

    private static HashSet<string> LoadGlobalFavoriteKeys()
    {
        try
        {
            var json = Preferences.Default.Get(GlobalFavoritesPreferenceKey, "[]");
            return JsonSerializer.Deserialize<HashSet<string>>(json) ?? new HashSet<string>();
        }
        catch (JsonException)
        {
            return new HashSet<string>();
        }
    }

    [RelayCommand]
    private async Task SelectPid(LivePidItem? item)
    {
        try
        {
        if (item is null)
            return;

        SelectedPid = item;
        HasSelectedPid = true;

        if (!_bluetooth.IsConnected)
        {
            _engine.SetFocusedPid(null);
            SelectedPidStatus = "Not Supported — OBD ადაპტერი არ არის დაკავშირებული.";
            SelectedPidStatusColor = "#F59E0B";
            StatusMessage = "არჩეული PID-ის წასაკითხად ჯერ დააკავშირე OBD ადაპტერი.";
            return;
        }

        try
        {
            if (item.IsOem)
            {
                if (!_oemLiveData.IsRunning)
                {
                    SelectedPidStatus = "OEM Live Data გაჩერებულია — დააჭირე OEM Data-ს.";
                    SelectedPidStatusColor = "#F59E0B";
                    return;
                }
                SelectedPidStatus = "Reviewed OEM DID რეალურ დროში ახლდება.";
                SelectedPidStatusColor = "#4ADE80";
                StatusMessage = $"{item.IdentifierText} · {item.Source}";
                return;
            }

            if (!_engine.IsRunning)
                await _engine.StartAsync();

            if (!_obd.IsPidSupported(item.Pid))
            {
                _engine.SetFocusedPid(null);
                SelectedPidStatus = "Not Supported — ამ PID-ს მიმდინარე ECU არ უჭერს მხარს.";
                SelectedPidStatusColor = "#F59E0B";
                StatusMessage = $"PID 01{item.Pid:X2} ამ ECU-ზე მიუწვდომელია.";
                return;
            }

            _engine.SetFocusedPid(item.Pid);
            IsPaused = false;
            SelectedPidStatus = "ECU-ის რეალური პასუხის მოლოდინი…";
            SelectedPidStatusColor = "#60A5FA";
            StatusMessage = $"PID 01{item.Pid:X2} არჩეულია Live Data-სთვის.";
            UpdateHeader();
        }
        catch (OperationCanceledException)
        {
            SelectedPidStatus = "Not Supported — PID-ის კითხვა შეწყდა.";
            SelectedPidStatusColor = "#F59E0B";
        }
        catch
        {
            _engine.SetFocusedPid(null);
            SelectedPidStatus = "Not Supported — ამ PID-ის წაკითხვა ვერ დაიწყო.";
            SelectedPidStatusColor = "#F59E0B";
            StatusMessage = "არჩეული PID-ის წაკითხვა ვერ დაიწყო. სცადე თავიდან.";
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "LiveDataViewModel.SelectPid");
        }
    }

    [RelayCommand]
    private void ResetGraph()
    {
        _liveGraph.Clear();
        GraphSamples = Array.Empty<ObdTelemetrySample>();
        foreach (var item in Pids) item.ResetStatistics();
        LastSnapshotText = "მონაცემები და სტატისტიკა გასუფთავდა";
    }

    [RelayCommand]
    private void Snapshot()
    {
        LastSnapshotText = $"Snapshot: {DateTime.Now:HH:mm:ss} · {_engine.LastSuccessfulPidCount} PID";
        StatusMessage = "მიმდინარე მნიშვნელობები დაფიქსირებულია ეკრანზე.";
    }

    [RelayCommand]
    private async Task ExportCsv()
    {
        try
        {
        if (_recorder.Samples.Count == 0)
        {
            StatusMessage = "ჯერ დაიწყე Record, რომ CSV შეინახოს.";
            return;
        }

        var path = Path.Combine(FileSystem.CacheDirectory, $"live-data-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        await File.WriteAllTextAsync(path, _recorder.ExportCsv());
        await Share.Default.RequestAsync(new ShareFileRequest { Title = "Live Data CSV", File = new ShareFile(path) });
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "LiveDataViewModel.ExportCsv");
        }
    }

    [RelayCommand]
    private async Task ExportJson()
    {
        try
        {
        if (_recorder.Samples.Count == 0)
        {
            StatusMessage = "ჯერ დაიწყე Record, რომ JSON შეინახოს.";
            return;
        }

        var path = Path.Combine(FileSystem.CacheDirectory, $"live-data-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, _recorder.ExportJson());
        await Share.Default.RequestAsync(new ShareFileRequest { Title = "Live Data JSON", File = new ShareFile(path) });
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "LiveDataViewModel.ExportJson");
        }
    }

    private void SyncDashboard()
    {
        DashboardPids.Clear();
        foreach (var item in Pids.Where(item => item.IsDashboard))
            DashboardPids.Add(item);
        _engine.SetDashboardPids(DashboardPids.Where(item => !item.IsOem).Select(item => item.Pid));
    }

    private void ApplySavedLayoutForVehicle()
    {
        var vin = CurrentVehicleVin();
        if (vin is null || string.Equals(_layoutAppliedVin, vin, StringComparison.OrdinalIgnoreCase))
            return;

        var layout = LoadLayoutForCurrentVehicle();
        if (layout is null)
        {
            _layoutAppliedVin = vin;
            return;
        }

        var globalFavorites = LoadGlobalFavoriteKeys();
        foreach (var item in Pids)
        {
            item.IsFavorite = globalFavorites.Contains(FavoriteKey(item));
            item.IsDashboard = layout.DashboardPids.Contains(item.Pid);
            item.IsGraph = layout.GraphPids.Contains(item.Pid);
        }

        SyncDashboard();
        RebuildVisiblePids();
        GraphPidIds = Pids.Where(item => item.IsGraph).Select(item => item.Pid).Take(3).ToArray();
        _liveGraph.Clear();
        GraphSamples = Array.Empty<ObdTelemetrySample>();
        _layoutAppliedVin = vin;
        StatusMessage = "ამ VIN-ის შენახული Live Dashboard აღდგა.";
    }

    private void SaveLayoutForCurrentVehicle()
    {
        var vin = CurrentVehicleVin();
        if (vin is null)
        {
            StatusMessage = "Dashboard-ის შესანახად ჯერ მანქანის რეალური VIN უნდა წაიკითხოს აპმა.";
            return;
        }

        var layout = new LiveDataLayout
        {
            FavoritePids = Pids.Where(item => !item.IsOem && item.IsFavorite).Select(item => item.Pid).ToList(),
            DashboardPids = Pids.Where(item => !item.IsOem && item.IsDashboard).Select(item => item.Pid).ToList(),
            GraphPids = Pids.Where(item => !item.IsOem && item.IsGraph).Select(item => item.Pid).Take(3).ToList()
        };
        Preferences.Default.Set(LayoutPreferencePrefix + vin, JsonSerializer.Serialize(layout));
        _layoutAppliedVin = vin;
    }

    private LiveDataLayout? LoadLayoutForCurrentVehicle()
    {
        var vin = CurrentVehicleVin();
        if (vin is null) return null;
        try
        {
            var json = Preferences.Default.Get(LayoutPreferencePrefix + vin, string.Empty);
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<LiveDataLayout>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void MarkLayoutAppliedForCurrentVehicle()
    {
        var vin = CurrentVehicleVin();
        if (vin is not null) _layoutAppliedVin = vin;
    }

    private string? CurrentVehicleVin()
    {
        var vin = _vehicle.CurrentVehicle?.VIN;
        return string.IsNullOrWhiteSpace(vin) ||
               vin.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
               vin.Length != 17
            ? null
            : vin;
    }

    private static bool IsCorePid(int pid) => pid is 0x0C or 0x0D or 0x05 or 0x04 or 0x11 or 0x42;

    [RelayCommand]
    private async Task Refresh()
    {
        try
        {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            await _oemLiveData.StopAsync();
            await _engine.StartAsync();
            EnsurePidCatalog();
            IsPaused = false;
            UpdateHeader();
        }
        catch (Exception ex)
        {
            StatusMessage = CarScanner.Services.UserError.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "LiveDataViewModel.Refresh");
        }
    }

    [RelayCommand]
    private async Task StartLiveData()
    {
        try
        {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            StatusMessage = "Live data მიმდინარეობს...";

            await _oemLiveData.StopAsync();
            await _engine.StartAsync();
            EnsurePidCatalog();
            IsPaused = false;
            UpdateHeader();
        }
        catch (Exception ex)
        {
            StatusMessage = CarScanner.Services.UserError.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "LiveDataViewModel.StartLiveData");
        }
    }

    [RelayCommand]
    private async Task PauseLiveData()
    {
        try
        {
        await _engine.StopAsync();
        IsPaused = true;
        StatusMessage = "Live data დაპაუზებულია";
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "LiveDataViewModel.PauseLiveData");
        }
    }

    [RelayCommand]
    private async Task StopLiveData()
    {
        try
        {
        await _engine.StopAsync();
        await _oemLiveData.StopAsync();

        if (IsRecording)
            await StopRecording();

        IsPaused = true;
        StatusMessage = "Live data შეჩერებულია";
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "LiveDataViewModel.StopLiveData");
        }
    }

    [RelayCommand]
    private async Task StartOemLiveData()
    {
        try
        {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            StatusMessage = "Verified OEM ECU და Live Data DID-ები მოწმდება…";
            await _engine.StopAsync();
            var vehicle = _vehicle.CurrentVehicle ?? await _vehicle.IdentifyConnectedVehicleAsync();
            var result = await _oemLiveData.StartAsync(vehicle);
            IsPaused = !(_oemLiveData.IsRunning);
            SourceMessage = result.Detail;
            StatusMessage = result.Status == "LOADING" ? "OEM Live Data იწყება…" : result.Detail;
        }
        catch (Exception exception)
        {
            SourceMessage = CarScanner.Services.UserError.Describe(exception);
            StatusMessage = "OEM Live Data ვერ დაიწყო; გამოგონილი მონაცემი არ გამოჩნდება.";
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "LiveDataViewModel.StartOemLiveData");
        }
    }

    [RelayCommand]
    private void StartRecording()
    {
        _recorder.Start();

        IsRecording = true;
        StatusMessage = "ჩაწერა დაიწყო — CSV/JSON შემდეგ ხელმისაწვდომი იქნება.";
    }

    [RelayCommand]
    private async Task StopRecording()
    {
        try
        {
        _recorder.Stop();
        IsRecording = false;
        var samples = _recorder.Samples;
        var vehicle = _vehicle.CurrentVehicle;
        var vehicleName = vehicle is null || vehicle.Manufacturer == "Unknown"
            ? _brand.Current.DisplayName
            : string.Join(" ", new[] { vehicle.Manufacturer, vehicle.Model, vehicle.Year }
                .Where(value => !string.IsNullOrWhiteSpace(value) && value != "Unknown"));
        var saved = await _recordingSessions.SaveAsync(samples, vehicleName, vehicle?.VIN ?? string.Empty, RoundRecordingValues);
        if (saved is null)
        {
            LastRecordingText = "სესია ცარიელია — ECU-დან მონაცემი არ მოსულა.";
            StatusMessage = LastRecordingText;
            return;
        }

        LastRecordingText = $"შენახულია: {saved.SampleCount} მონაცემი · {saved.FileSizeText}";
        StatusMessage = $"ჩაწერა დასრულდა და მოწყობილობაზე შეინახა: {saved.SampleCount} მონაცემი.";
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "LiveDataViewModel.StopRecording");
        }
    }

    partial void OnRoundRecordingValuesChanged(bool value) => Preferences.Default.Set("round_live_recording_values", value);

    [RelayCommand]
    // Recordings now live in Graph Studio (old RecordingHistoryPage removed).
    private Task OpenRecordingHistory() => CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.GraphStudioPage));

    public IReadOnlyList<ObdTelemetrySample> GetRecording()
    {
        return _recorder.Samples;
    }

    private static IEnumerable<ObdTelemetrySample> ToSamples(
        LiveDataModel d)
    {
        var t = d.Timestamp;

        if (d.HasRpm)
        {
            yield return new ObdTelemetrySample
            {
                Timestamp = t,
                Pid = 0x0C,
                Name = "RPM",
                Unit = "rpm",
                Value = d.Rpm
            };
        }

        if (d.HasSpeed)
        {
            yield return new ObdTelemetrySample
            {
                Timestamp = t,
                Pid = 0x0D,
                Name = "Speed",
                Unit = "km/h",
                Value = d.Speed
            };
        }

        if (d.HasCoolantTemp)
        {
            yield return new ObdTelemetrySample
            {
                Timestamp = t,
                Pid = 0x05,
                Name = "Coolant",
                Unit = "°C",
                Value = d.CoolantTemp
            };
        }

        if (d.HasVoltage)
        {
            yield return new ObdTelemetrySample
            {
                Timestamp = t,
                Pid = 0x42,
                Name = "Voltage",
                Unit = "V",
                Value = d.Voltage
            };
        }

        if (d.HasEngineLoad)
        {
            yield return new ObdTelemetrySample
            {
                Timestamp = t,
                Pid = 0x04,
                Name = "Engine load",
                Unit = "%",
                Value = d.EngineLoad
            };
        }

        if (d.HasThrottlePosition)
        {
            yield return new ObdTelemetrySample
            {
                Timestamp = t,
                Pid = 0x11,
                Name = "Throttle",
                Unit = "%",
                Value = d.ThrottlePosition
            };
        }
    }
}
