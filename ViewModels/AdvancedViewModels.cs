using CarScanner.AI;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.OemDiagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
namespace CarScanner.ViewModels;

public partial class EmissionsViewModel : BaseViewModel
{
    private readonly EmissionService _service;
    private readonly UnitSystemService _units;
    private readonly LocalizationService _localization;
    private EmissionReadinessReport? _lastReport;

    public ObservableCollection<EmissionMonitorDisplayItem> Monitors { get; } = new();
    public ObservableCollection<EmissionMonitorDisplayItem> NonContinuousMonitors { get; } = new();
    public ObservableCollection<EmissionMonitorDisplayItem> DriveCycleMonitors { get; } = new();
    public ObservableCollection<EmissionMonitorDisplayItem> ReadinessMonitors { get; } = new();

    [ObservableProperty] private string summary = "";
    [ObservableProperty] private string inspectionStatus = "";
    [ObservableProperty] private string inspectionColor = "#94A3B8";
    [ObservableProperty] private string inspectionGlyph = "?";
    [ObservableProperty] private string milStatus = "";
    [ObservableProperty] private string emissionDtcStatus = "";
    [ObservableProperty] private string readinessRatio = "— / —";
    [ObservableProperty] private string readinessCountCaption = "";
    [ObservableProperty] private string incompleteMonitorCount = "—";
    [ObservableProperty] private string distanceSinceClear = "—";
    [ObservableProperty] private string warmUpsSinceClear = "—";
    [ObservableProperty] private string distanceWithMil = "—";
    [ObservableProperty] private string timeWithMil = "—";
    [ObservableProperty] private string timeSinceClear = "—";

    private string pageTitle = "";
    public string PageTitle
    {
        get => pageTitle;
        private set => SetProperty(ref pageTitle, value);
    }
    private string headerTitle = "";
    public string HeaderTitle
    {
        get => headerTitle;
        private set => SetProperty(ref headerTitle, value);
    }
    private string headerSubtitle = "";
    public string HeaderSubtitle
    {
        get => headerSubtitle;
        private set => SetProperty(ref headerSubtitle, value);
    }
    private string refreshButtonText = "";
    public string RefreshButtonText
    {
        get => refreshButtonText;
        private set => SetProperty(ref refreshButtonText, value);
    }
    private string mode05ButtonText = "MODE 05";
    public string Mode05ButtonText
    {
        get => mode05ButtonText;
        private set => SetProperty(ref mode05ButtonText, value);
    }
    private string mode06ButtonText = "MODE 06";
    public string Mode06ButtonText
    {
        get => mode06ButtonText;
        private set => SetProperty(ref mode06ButtonText, value);
    }
    private string readinessStatusLabel = "";
    public string ReadinessStatusLabel
    {
        get => readinessStatusLabel;
        private set => SetProperty(ref readinessStatusLabel, value);
    }
    private string milStatusLabel = "";
    public string MilStatusLabel
    {
        get => milStatusLabel;
        private set => SetProperty(ref milStatusLabel, value);
    }
    private string emissionDtcLabel = "";
    public string EmissionDtcLabel
    {
        get => emissionDtcLabel;
        private set => SetProperty(ref emissionDtcLabel, value);
    }
    private string sinceDtcClearTitle = "";
    public string SinceDtcClearTitle
    {
        get => sinceDtcClearTitle;
        private set => SetProperty(ref sinceDtcClearTitle, value);
    }
    private string sinceDtcClearSubtitle = "";
    public string SinceDtcClearSubtitle
    {
        get => sinceDtcClearSubtitle;
        private set => SetProperty(ref sinceDtcClearSubtitle, value);
    }
    private string distanceSinceClearLabel = "";
    public string DistanceSinceClearLabel
    {
        get => distanceSinceClearLabel;
        private set => SetProperty(ref distanceSinceClearLabel, value);
    }
    private string warmUpsLabel = "";
    public string WarmUpsLabel
    {
        get => warmUpsLabel;
        private set => SetProperty(ref warmUpsLabel, value);
    }
    private string distanceWithMilLabel = "";
    public string DistanceWithMilLabel
    {
        get => distanceWithMilLabel;
        private set => SetProperty(ref distanceWithMilLabel, value);
    }
    private string timeWithMilLabel = "";
    public string TimeWithMilLabel
    {
        get => timeWithMilLabel;
        private set => SetProperty(ref timeWithMilLabel, value);
    }
    private string continuousMonitorsTitle = "";
    public string ContinuousMonitorsTitle
    {
        get => continuousMonitorsTitle;
        private set => SetProperty(ref continuousMonitorsTitle, value);
    }
    private string continuousMonitorsSubtitle = "";
    public string ContinuousMonitorsSubtitle
    {
        get => continuousMonitorsSubtitle;
        private set => SetProperty(ref continuousMonitorsSubtitle, value);
    }
    private string nonContinuousMonitorsTitle = "";
    public string NonContinuousMonitorsTitle
    {
        get => nonContinuousMonitorsTitle;
        private set => SetProperty(ref nonContinuousMonitorsTitle, value);
    }
    private string nonContinuousMonitorsSubtitle = "";
    public string NonContinuousMonitorsSubtitle
    {
        get => nonContinuousMonitorsSubtitle;
        private set => SetProperty(ref nonContinuousMonitorsSubtitle, value);
    }
    private string currentDriveCycleTitle = "";
    public string CurrentDriveCycleTitle
    {
        get => currentDriveCycleTitle;
        private set => SetProperty(ref currentDriveCycleTitle, value);
    }
    private string currentDriveCycleSubtitle = "";
    public string CurrentDriveCycleSubtitle
    {
        get => currentDriveCycleSubtitle;
        private set => SetProperty(ref currentDriveCycleSubtitle, value);
    }
    private string infoNoteText = "";
    public string InfoNoteText
    {
        get => infoNoteText;
        private set => SetProperty(ref infoNoteText, value);
    }
    private string loadingTitle = "";
    public string LoadingTitle
    {
        get => loadingTitle;
        private set => SetProperty(ref loadingTitle, value);
    }
    private string loadingSubtitle = "";
    public string LoadingSubtitle
    {
        get => loadingSubtitle;
        private set => SetProperty(ref loadingSubtitle, value);
    }
    private string incompleteMonitorsLabel = "";
    public string IncompleteMonitorsLabel
    {
        get => incompleteMonitorsLabel;
        private set => SetProperty(ref incompleteMonitorsLabel, value);
    }
    private string monitorsTitle = "";
    public string MonitorsTitle
    {
        get => monitorsTitle;
        private set => SetProperty(ref monitorsTitle, value);
    }
    private string obdDisclaimerText = "";
    public string ObdDisclaimerText
    {
        get => obdDisclaimerText;
        private set => SetProperty(ref obdDisclaimerText, value);
    }

    public EmissionsViewModel(
        EmissionService service,
        UnitSystemService units,
        LocalizationService localization)
    {
        _service = service;
        _units = units;
        _localization = localization;

        Attach();

        UpdateLanguageTexts();
        RefreshReportPresentation();
    }

    // Singleton services must not keep this transient view model (and its
    // page) alive: the page calls Attach() on appearing and Detach() on leaving.
    private bool _attached;

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _units.UnitSystemChanged += OnUnitSystemChanged;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _units.UnitSystemChanged -= OnUnitSystemChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnUnitSystemChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshUnitPresentation);

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateLanguageTexts();
            RefreshReportPresentation();
            RebuildMonitorLists();
            RefreshUnitPresentation();
        });

    private void UpdateLanguageTexts()
    {
        if (_localization.IsEnglish)
        {
            PageTitle = "Inspection Readiness";
            HeaderTitle = "INSPECTION READINESS";
            HeaderSubtitle = "OBD-II emissions & readiness monitors";
            RefreshButtonText = "Refresh";
            Mode05ButtonText = "MODE 05";
            Mode06ButtonText = "MODE 06";
            ReadinessStatusLabel = "READINESS STATUS";
            MilStatusLabel = "MIL STATUS";
            EmissionDtcLabel = "STORED DTC";
            SinceDtcClearTitle = "Since DTC Clear";
            SinceDtcClearSubtitle = "ECU counters recorded since diagnostic codes were cleared";
            DistanceSinceClearLabel = "DISTANCE SINCE CLEAR";
            WarmUpsLabel = "WARM-UPS";
            DistanceWithMilLabel = "DISTANCE · MIL ON";
            TimeWithMilLabel = "TIME · MIL ON";
            ContinuousMonitorsTitle = "Continuous Monitors";
            ContinuousMonitorsSubtitle = "Monitors checked continuously while the engine is operating";
            NonContinuousMonitorsTitle = "Non-continuous Monitors";
            NonContinuousMonitorsSubtitle = "These monitors usually require specific driving conditions";
            CurrentDriveCycleTitle = "Current Drive Cycle";
            CurrentDriveCycleSubtitle = "Current-cycle readiness reported by optional Mode 01 PID 41";
            InfoNoteText = "Inspection requirements vary by country and region. This page shows the OBD-II readiness status reported by the ECU.";
            LoadingTitle = "Updating ECU data...";
            LoadingSubtitle = "Reading readiness monitors and emissions status";
            IncompleteMonitorsLabel = "INCOMPLETE MONITORS";
            MonitorsTitle = "MONITORS";
            ReadinessCountCaption = "monitors complete";
            ObdDisclaimerText = "Preliminary OBD check — does not replace an official emissions inspection.";
        }
        else
        {
            PageTitle = "ინსპექციის მზადყოფნა";
            HeaderTitle = "ინსპექციის მზადყოფნა";
            HeaderSubtitle = "OBD-II ემისიები და readiness მონიტორები";
            RefreshButtonText = "განახლება";
            Mode05ButtonText = "MODE 05";
            Mode06ButtonText = "MODE 06";
            ReadinessStatusLabel = "მზადყოფნის სტატუსი";
            MilStatusLabel = "MIL სტატუსი";
            EmissionDtcLabel = "შენახული DTC";
            SinceDtcClearTitle = "DTC-ის წაშლის შემდეგ";
            SinceDtcClearSubtitle = "ECU-ის მონაცემები დიაგნოსტიკური კოდების ბოლო წაშლის შემდეგ";
            DistanceSinceClearLabel = "მანძილი წაშლის შემდეგ";
            WarmUpsLabel = "გახურების ციკლები";
            DistanceWithMilLabel = "მანძილი · MIL ჩართული";
            TimeWithMilLabel = "დრო · MIL ჩართული";
            ContinuousMonitorsTitle = "უწყვეტი მონიტორები";
            ContinuousMonitorsSubtitle = "მონიტორები, რომლებიც ძრავის მუშაობისას მუდმივად მოწმდება";
            NonContinuousMonitorsTitle = "არაუწყვეტი მონიტორები";
            NonContinuousMonitorsSubtitle = "ამ მონიტორებს კონკრეტული მართვის პირობები სჭირდება";
            CurrentDriveCycleTitle = "მიმდინარე მართვის ციკლი";
            CurrentDriveCycleSubtitle = "მიმდინარე ციკლის readiness — optional Mode 01 PID 41";
            InfoNoteText = "ინსპექციის წესები ქვეყნების და რეგიონების მიხედვით განსხვავდება. ეს გვერდი აჩვენებს ECU-ის მიერ დაბრუნებულ OBD-II readiness სტატუსს.";
            LoadingTitle = "ECU მონაცემები ახლდება...";
            LoadingSubtitle = "Readiness მონიტორები და ემისიის სტატუსი იკითხება";
            IncompleteMonitorsLabel = "დაუსრულებელი მონიტორები";
            MonitorsTitle = "მონიტორები";
            ReadinessCountCaption = "მონიტორი დასრულებულია";
            ObdDisclaimerText = "წინასწარი OBD შემოწმება — არ ცვლის ოფიციალურ ტექინსპექტირებას.";
        }

        Title = PageTitle;
    }

    [RelayCommand]
    public async Task Load()
    {
        try
        {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;

            Monitors.Clear();
            NonContinuousMonitors.Clear();
            DriveCycleMonitors.Clear();
            ReadinessMonitors.Clear();

            _lastReport = await _service.ReadAsync();

            RebuildMonitorLists();
            RefreshReportPresentation();
            RefreshUnitPresentation();

            StatusMessage = _localization.IsEnglish
                ? "Tests updated"
                : "ტესტები განახლდა";
        }
        catch (Exception ex)
        {
            StatusMessage = UserError.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "EmissionsViewModel.Load");
        }
    }

    private void RefreshReportPresentation()
    {
        if (_lastReport is null)
        {
            InspectionStatus = _localization.IsEnglish
                ? "NOT CHECKED"
                : "ჯერ არ შემოწმებულა";

            InspectionColor = "#94A3B8";
            InspectionGlyph = "?";

            MilStatus = _localization.IsEnglish
                ? "Unavailable"
                : "მიუწვდომელია";

            EmissionDtcStatus = _localization.IsEnglish
                ? "Unavailable"
                : "მიუწვდომელია";

            ReadinessRatio = "— / —";
            IncompleteMonitorCount = "—";

            Summary = _localization.IsEnglish
                ? "Tap Refresh to check the readiness monitors."
                : "Readiness მონიტორების შესამოწმებლად დააჭირე „განახლება“-ს.";

            return;
        }

        var readiness = _lastReport.SinceClear;
        var hasIncomplete = _lastReport.SinceClearMonitors.Any(
            x => x.Supported == true && x.Complete == false);
        var supportedCount = _lastReport.SinceClearMonitors.Count(x => x.Supported == true);
        var completedCount = _lastReport.SinceClearMonitors.Count(
            x => x.Supported == true && x.Complete == true);
        var incompleteCount = Math.Max(0, supportedCount - completedCount);

        ReadinessRatio = readiness is null ? "— / —" : $"{completedCount} / {supportedCount}";
        IncompleteMonitorCount = readiness is null ? "—" : incompleteCount.ToString();
        ReadinessCountCaption = _localization.IsEnglish
            ? (completedCount == 1 ? "monitor complete" : "monitors complete")
            : "მონიტორი დასრულებულია";

        var ready =
            readiness is not null &&
            !readiness.MilOn &&
            readiness.StoredDtcCount == 0 &&
            !hasIncomplete;

        if (readiness is null)
        {
            InspectionStatus = _localization.IsEnglish
                ? "Readiness unavailable"
                : "მზადყოფნა მიუწვდომელია";

            InspectionColor = "#94A3B8";
            InspectionGlyph = "?";
        }
        else if (ready)
        {
            InspectionStatus = _localization.IsEnglish
                ? "Ready for the emissions test"
                : "ტესტისთვის მზადაა";

            InspectionColor = "#4ADE80";
            InspectionGlyph = "✓";
        }
        else
        {
            InspectionStatus = _localization.IsEnglish
                ? "Not ready yet"
                : "ჯერ არ არის მზად";

            InspectionColor = "#FBBF24";
            InspectionGlyph = "!";
        }

        MilStatus = readiness is null
            ? (_localization.IsEnglish
                ? "Unavailable"
                : "მიუწვდომელია")
            : _localization.IsEnglish
                ? (readiness.MilOn ? "ON" : "OFF")
                : (readiness.MilOn ? "ჩართულია" : "გამორთულია");

        EmissionDtcStatus = readiness is null
            ? (_localization.IsEnglish
                ? "Unavailable"
                : "მიუწვდომელია")
            : readiness.StoredDtcCount.ToString();

        if (readiness is null)
        {
            Summary = _localization.IsEnglish
                ? "The ECU did not return readiness data. This does not automatically indicate a fault."
                : "ECU-მ Readiness პასუხი არ დააბრუნა. ეს ავტომატურად გაუმართაობას არ ნიშნავს.";
        }
        else if (ready)
        {
            Summary = _localization.IsEnglish
                ? "All supported monitors are complete and the MIL is off."
                : "ყველა მხარდაჭერილი მონიტორი დასრულებულია და MIL გამორთულია.";
        }
        else
        {
            Summary = _localization.IsEnglish
                ? "Some supported monitors are incomplete or MIL/DTC is active. A drive cycle and another check may be required."
                : "ზოგი მხარდაჭერილი მონიტორი ჯერ დაუსრულებელია ან MIL/DTC აქტიურია. საჭიროა drive cycle და ხელახალი შემოწმება.";
        }
    }

    private void RebuildMonitorLists()
    {
        Monitors.Clear();
        NonContinuousMonitors.Clear();
        DriveCycleMonitors.Clear();
        ReadinessMonitors.Clear();

        if (_lastReport is null)
            return;

        foreach (var monitor in _lastReport.SinceClearMonitors
                     .Where(x => x.Supported == true))
        {
            ReadinessMonitors.Add(ToDisplayMonitor(monitor));
        }

        foreach (var monitor in _lastReport.SinceClearMonitors
                     .Where(x => x.Category == "Continuous"))
        {
            Monitors.Add(ToDisplayMonitor(monitor));
        }

        foreach (var monitor in _lastReport.SinceClearMonitors
                     .Where(x => x.Category == "Non-continuous"))
        {
            NonContinuousMonitors.Add(ToDisplayMonitor(monitor));
        }

        foreach (var monitor in _lastReport.DriveCycleMonitors)
        {
            DriveCycleMonitors.Add(ToDisplayMonitor(monitor));
        }
    }

    private EmissionMonitorDisplayItem ToDisplayMonitor(EmissionMonitor monitor)
    {
        return new EmissionMonitorDisplayItem
        {
            Name = LocalizeMonitorName(monitor.Name),
            Category = LocalizeMonitorText(monitor.Category),
            Details = LocalizeMonitorText(monitor.Details),
            Status = monitor.Supported == true && monitor.Complete == true
                ? (_localization.IsEnglish ? "Ready" : "მზადაა")
                : LocalizeMonitorText(monitor.Status),
            Glyph = monitor.Complete == true ? "✓" : "!",
            StatusColor = ConvertStatusColor(monitor.StatusColor)
        };
    }

    private static Color ConvertStatusColor(object? value)
    {
        if (value is Color color)
            return color;

        if (value is string text &&
            !string.IsNullOrWhiteSpace(text))
        {
            try
            {
                return Color.FromArgb(text);
            }
            catch
            {
            }
        }

        return Color.FromArgb("#94A3B8");
    }

    private string LocalizeMonitorName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";

        var text = value.Trim();

        if (_localization.IsEnglish)
        {
            return text switch
            {
                "აალების გამოტოვება" => "Misfire",
                "საწვავის სისტემა" => "Fuel System",
                "ძირითადი კომპონენტები" => "Comprehensive Components",
                "კატალიზატორი" => "Catalyst",
                "გათბობადი კატალიზატორი" => "Heated Catalyst",
                "აორთქლების სისტემა (EVAP)" => "Evaporative System",
                "მეორადი ჰაერის სისტემა" => "Secondary Air System",
                "კონდიციონერის სისტემა" => "A/C Refrigerant",
                "ჟანგბადის სენსორი" => "Oxygen Sensor",
                "ჟანგბადის სენსორის გამათბობელი" => "Oxygen Sensor Heater",
                "EGR სისტემა" => "EGR System",
                "EGR / VVT სისტემა" => "EGR / VVT System",
                "NMHC კატალიზატორი" => "NMHC Catalyst",
                "NOx / SCR სისტემა" => "NOx/SCR",
                "ბუსტის წნევა" => "Boost Pressure",
                "გამონაბოლქვის გაზის სენსორი" => "Exhaust Gas Sensor",
                "ნაწილაკების ფილტრი" => "PM Filter",
                _ => text
            };
        }

        return text switch
        {
            "Misfire" => "აალების გამოტოვება",
            "Fuel System" => "საწვავის სისტემა",
            "Comprehensive Components" => "ძირითადი კომპონენტები",
            "Catalyst" => "კატალიზატორი",
            "Heated Catalyst" => "გათბობადი კატალიზატორი",
            "Evaporative System" => "აორთქლების სისტემა (EVAP)",
            "Secondary Air System" => "მეორადი ჰაერის სისტემა",
            "A/C Refrigerant" => "კონდიციონერის სისტემა",
            "Oxygen Sensor" => "ჟანგბადის სენსორი",
            "Oxygen Sensor Heater" => "ჟანგბადის სენსორის გამათბობელი",
            "EGR System" => "EGR სისტემა",
            "EGR/VVT System" => "EGR / VVT სისტემა",
            "EGR / VVT System" => "EGR / VVT სისტემა",
            "NMHC Catalyst" => "NMHC კატალიზატორი",
            "NOx/SCR" => "NOx / SCR სისტემა",
            "Boost Pressure" => "ბუსტის წნევა",
            "Exhaust Gas Sensor" => "გამონაბოლქვის გაზის სენსორი",
            "PM Filter" => "ნაწილაკების ფილტრი",
            _ => text
        };
    }

    private string LocalizeMonitorText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";

        var text = value.Trim();

        if (_localization.IsEnglish)
        {
            return text switch
            {
                "დასრულებულია" => "Complete",
                "დაუსრულებელია" => "Incomplete",
                "არ არის მხარდაჭერილი" => "Not Supported",
                "მხარდაჭერილია" => "Supported",
                "უწყვეტი" => "Continuous",
                "არაუწყვეტი" => "Non-continuous",
                "მიუწვდომელია" => "Unavailable",
                "მზადაა" => "Ready",
                "ჯერ არ არის მზად" => "Not Ready",
                _ => text
            };
        }

        return text switch
        {
            "Complete" => "დასრულებულია",
            "Completed" => "დასრულებულია",
            "Incomplete" => "დაუსრულებელია",
            "Not Supported" => "არ არის მხარდაჭერილი",
            "Unsupported" => "არ არის მხარდაჭერილი",
            "Supported" => "მხარდაჭერილია",
            "Available" => "ხელმისაწვდომია",
            "Unavailable" => "მიუწვდომელია",
            "Ready" => "მზადაა",
            "Not Ready" => "ჯერ არ არის მზად",
            "Continuous" => "უწყვეტი",
            "Non-continuous" => "არაუწყვეტი",
            _ => text
        };
    }

    private void RefreshUnitPresentation()
    {
        if (_lastReport is null)
            return;

        DistanceSinceClear = _lastReport.DistanceSinceClearKm.HasValue
            ? _units.FormatDistance(_lastReport.DistanceSinceClearKm.Value, "0")
            : LocalizeUnavailableValue(_lastReport.DistanceSinceClear);

        WarmUpsSinceClear = _lastReport.WarmUpsSinceClearValue.HasValue
            ? _localization.IsEnglish
                ? $"{_lastReport.WarmUpsSinceClearValue.Value:0} warm-ups"
                : $"{_lastReport.WarmUpsSinceClearValue.Value:0} გახურება"
            : LocalizeUnavailableValue(_lastReport.WarmUpsSinceClear);

        DistanceWithMil = _lastReport.DistanceWithMilKm.HasValue
            ? _units.FormatDistance(_lastReport.DistanceWithMilKm.Value, "0")
            : LocalizeUnavailableValue(_lastReport.DistanceWithMil);

        TimeWithMil = _lastReport.TimeWithMilMinutes.HasValue
            ? _localization.IsEnglish
                ? $"{_lastReport.TimeWithMilMinutes.Value:0} min"
                : $"{_lastReport.TimeWithMilMinutes.Value:0} წთ"
            : LocalizeUnavailableValue(_lastReport.TimeWithMil);

        TimeSinceClear = _lastReport.TimeSinceClearMinutes.HasValue
            ? _localization.IsEnglish
                ? $"{_lastReport.TimeSinceClearMinutes.Value:0} min"
                : $"{_lastReport.TimeSinceClearMinutes.Value:0} წთ"
            : LocalizeUnavailableValue(_lastReport.TimeSinceClear);
    }

    private string LocalizeUnavailableValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";

        var text = value.Trim();

        if (_localization.IsEnglish)
        {
            return text switch
            {
                "არ არის მხარდაჭერილი" => "Not Supported",
                "მიუწვდომელია" => "Unavailable",
                _ => text
            };
        }

        return text switch
        {
            "Not Supported" => "არ არის მხარდაჭერილი",
            "Unsupported" => "არ არის მხარდაჭერილი",
            "Unavailable" => "მიუწვდომელია",
            "Not Available" => "მიუწვდომელია",
            _ => text
        };
    }

    [RelayCommand]
    private Task OpenMode06() =>
        Shell.Current.GoToAsync(nameof(Views.Mode06Page));

    [RelayCommand]
    private Task OpenMode05() =>
        Shell.Current.GoToAsync(nameof(Views.Mode05Page));
}

public sealed class EmissionMonitorDisplayItem
{
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Details { get; init; } = "";
    public string Status { get; init; } = "";
    public string Glyph { get; init; } = "";

    public Color StatusColor { get; init; } =
        Color.FromArgb("#94A3B8");
}

public partial class EcuIdentifiersViewModel : BaseViewModel
{
    private const int PidBatchSize = 6;
    private readonly AdvancedDiagnosticService _service;
    private readonly OBDService _obd;
    private readonly VehicleInfoService _vehicle;
    private readonly UnitSystemService _units;
    private readonly OemEcuIdentificationService _oemIdentification;
    private readonly ModuleDtcScanService _modules;
    private readonly ProService _pro;
    private readonly SemaphoreSlim _pidRefreshGate = new(1, 1);
    private CancellationTokenSource? _pidPollingCts;
    private Task? _pidPollingTask;
    private int _pidCursor;

    public ObservableCollection<EcuIdentifier> Items { get; } = new();
    public ObservableCollection<OemIdentificationValue> OemItems { get; } = new();
    public ObservableCollection<EcuPidReading> PidReadings { get; } = new();
    [ObservableProperty] private string networkStatus = "სკანირების შემდეგ გამოჩნდება მხოლოდ რეალურად პასუხმგებელი ECU.";
    [ObservableProperty] private string vehicleIdentity = "VIN: —";
    [ObservableProperty] private string communication = "Protocol: —";
    [ObservableProperty] private string pidSummary = "Supported: — parameters";
    [ObservableProperty] private string pidRefreshStatus = "რეალური ECU მნიშვნელობების მოლოდინი…";
    [ObservableProperty] private string oemIdentificationStatus = "OEM identification ჯერ არ შემოწმებულა.";

    // Identification of every module (PRO).
    public ObservableCollection<ModuleInfoCard> ModuleInfos { get; } = new();
    [ObservableProperty] private bool isModuleInfoUnlocked;
    [ObservableProperty] private bool isModuleInfoLocked = true;
    [ObservableProperty] private bool needsModuleScan;
    [ObservableProperty] private string moduleInfoStatus = string.Empty;

    public EcuIdentifiersViewModel(
        AdvancedDiagnosticService service,
        OBDService obd,
        VehicleInfoService vehicle,
        UnitSystemService units,
        OemEcuIdentificationService oemIdentification,
        ModuleDtcScanService modules,
        ProService pro)
    {
        _modules = modules;
        _pro = pro;
        _service = service;
        _obd = obd;
        _vehicle = vehicle;
        _units = units;
        _oemIdentification = oemIdentification;
        Attach();
        Title = "ECU ინფორმაცია";
    }

    // Detached by EcuIdentifiersPage when it goes away (no leak through UnitSystemService).
    private bool _attached;

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _units.UnitSystemChanged += OnUnitSystemChanged;
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _units.UnitSystemChanged -= OnUnitSystemChanged;
    }

    private void OnUnitSystemChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshUnitPresentation);
    [RelayCommand]
    public async Task Load()
    {
        try
        {
        if (IsBusy) return;
        try
        {
            var shouldRestartPolling = _pidPollingCts is not null;
            await StopPidUpdatesAsync();

            IsBusy = true;
            Items.Clear();
            OemItems.Clear();
            PidReadings.Clear();
            _pidCursor = 0;

            if (!_obd.IsInitialized) await _obd.InitializeAsync();
            foreach (var item in await _service.ReadIdentifiersAsync()) Items.Add(item);

            var diagnostic = await _obd.GetAdapterDiagnosticsAsync();
            var vehicle = _vehicle.CurrentVehicle;

            // Prefer Vehicle Info, then use the ECM Mode 09 VIN read made by
            // this page. Home may still be doing its connection-time lookup.
            if (!HasKnownValue(vehicle?.VIN))
                vehicle = await _vehicle.IdentifyConnectedVehicleAsync();

            var ecmVin = Items
                .FirstOrDefault(item => item.Name.Contains("VIN", StringComparison.OrdinalIgnoreCase) && HasKnownValue(item.Value))
                ?.Value;
            var vin = FirstKnownValue(vehicle?.VIN, ecmVin);
            VehicleIdentity = $"VIN: {vin}\n{DisplayVehicleName(vehicle)}\nEngine: {DisplayValue(vehicle?.Engine)} · Fuel: {DisplayValue(vehicle?.FuelType)}";

            var protocol = FirstKnownValue(_obd.CurrentProtocolDescription, diagnostic.Protocol);
            Communication = $"Protocol: {protocol}\nAdapter: {diagnostic.AdapterName}\nResponse: {(diagnostic.VehicleResponding ? "ECM პასუხობს" : "ECM პასუხი ვერ დადასტურდა")} · {diagnostic.ProbeLatencyMs} ms\nCAN ID / OEM address: დადასტურებული მისამართი ჯერ არ არის";
            NetworkStatus = diagnostic.VehicleResponding ? "Engine Control Module — Communication OK" : "Engine ECU პასუხი ვერ დადასტურდა";

            if (vehicle is not null)
            {
                var oemResult = await _oemIdentification.ReadAsync(vehicle);
                foreach (var value in oemResult.Values)
                    OemItems.Add(value);
                OemIdentificationStatus = $"{oemResult.Status} — {oemResult.Detail}";
                if (OemItems.Count > 0)
                    NetworkStatus += $"\nOEM identification: {OemItems.Count} field result";
            }
            else
            {
                OemIdentificationStatus = "NOT AVAILABLE — Vehicle identity ვერ დადასტურდა.";
            }

            foreach (var pid in _obd.SupportedPids)
            {
                var reading = new EcuPidReading
                {
                    Pid = pid.Pid,
                    Name = pid.Name,
                    RawUnit = pid.Unit,
                    Category = pid.Category
                };
                reading.RefreshPresentation(_units);
                PidReadings.Add(reading);
            }

            await RefreshModuleInfoAsync();

            PidSummary = $"Supported: {PidReadings.Count} standard parameters";
            PidRefreshStatus = PidReadings.Count == 0
                ? "ECU-მ მხარდაჭერილი, დეკოდირებადი PID-ები არ დააბრუნა."
                : "Supported PID-ების რეალური მნიშვნელობები მბრუნავ რეჟიმში ახლდება.";
            StatusMessage = "ECU Explorer განახლდა";

            if (shouldRestartPolling)
                await StartPidUpdatesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = UserError.Describe(ex);
            NetworkStatus = "კავშირის შემოწმება ვერ შესრულდა";
            OemIdentificationStatus = "ERROR — OEM identification ვერ შესრულდა.";
        }
        finally { IsBusy = false; }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "EcuIdentifiersViewModel.Load");
        }
    }

    private static bool UiEnglish => CarScanner.Services.Localization.LanguageCatalog.UsesEnglishBase;

    /// <summary>Shows name, part number, versions and serial of every module from the last scan (PRO).</summary>
    public async Task RefreshModuleInfoAsync()
    {
        var english = UiEnglish;
        ModuleInfos.Clear();
        var unlocked = false;
        try { unlocked = await _pro.EnsureProAsync(); } catch { }
        IsModuleInfoUnlocked = unlocked;
        IsModuleInfoLocked = !unlocked;
        NeedsModuleScan = false;

        if (!unlocked)
        {
            ModuleInfoStatus = english
                ? "Name, part number, software and hardware version and serial number of every module (ABS, airbag, body, transmission, battery …)."
                : "ყველა მოდულის (ABS, აირბაგი, ძარა, კოლოფი, ბატარეა …) სახელი, ნაწილის ნომერი, პროგრამის და აპარატურის ვერსია, სერიული ნომერი.";
            return;
        }

        var modules = _modules.LastModules;
        if (modules.Count == 0)
        {
            NeedsModuleScan = true;
            ModuleInfoStatus = english
                ? "Run a full scan first — the modules are read during the scan."
                : "ჯერ გაუშვი სრული სკანირება — მოდულები სკანირებისას იკითხება.";
            return;
        }

        foreach (var module in modules)
        {
            var lines = module.Identity
                .Select(item => $"{(english ? item.NameEn : item.NameKa)}: {item.Value}")
                .ToList();
            if (lines.Count == 0 && !string.IsNullOrWhiteSpace(module.SoftwareVersion))
                lines.Add($"{(english ? "Software" : "პროგრამა")}: {module.SoftwareVersion}");

            var name = english ? module.NameEn : module.NameKa;
            ModuleInfos.Add(new ModuleInfoCard
            {
                Title = string.IsNullOrWhiteSpace(name) ? module.AddressText : name,
                Subtitle = string.Join(" · ", new[]
                {
                    string.IsNullOrWhiteSpace(module.EcuName) || module.EcuName == name ? null : module.EcuName,
                    module.Protocol is "" or "—" ? null : module.Protocol
                }.Where(part => !string.IsNullOrWhiteSpace(part))),
                Lines = lines.Count == 0
                    ? (english ? "The module did not report identification data." : "მოდულმა საიდენტიფიკაციო მონაცემები არ დააბრუნა.")
                    : string.Join("\n", lines)
            });
        }

        var time = _modules.LastScanAt?.ToString("HH:mm") ?? "—";
        ModuleInfoStatus = english
            ? $"{ModuleInfos.Count} modules · from the scan at {time}"
            : $"{ModuleInfos.Count} მოდული · {time}-ის სკანირებიდან";
    }

    [RelayCommand]
    private async Task UnlockModuleInfo()
    {
        try
        {
        if (await ProGate.EnsureAsync(_pro, "მოდულების ინფორმაცია", "Module information"))
            await RefreshModuleInfoAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "EcuIdentifiersViewModel.UnlockModuleInfo");
        }
    }

    [RelayCommand]
    private Task OpenModuleScan() => Shell.Current.GoToAsync(nameof(Views.ModuleScanPage));

    /// <summary>Begins bounded, rotating reads only while ECU Explorer is visible.</summary>
    public Task StartPidUpdatesAsync()
    {
        if (_pidPollingCts is not null || PidReadings.Count == 0)
            return Task.CompletedTask;

        var cts = new CancellationTokenSource();
        _pidPollingCts = cts;
        _pidPollingTask = PollPidValuesAsync(cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopPidUpdatesAsync()
    {
        var cts = _pidPollingCts;
        var task = _pidPollingTask;
        _pidPollingCts = null;
        _pidPollingTask = null;

        if (cts is null)
            return;

        cts.Cancel();
        try
        {
            if (task is not null)
                await task;
        }
        catch (OperationCanceledException) { }
        finally { cts.Dispose(); }
    }

    private async Task PollPidValuesAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await RefreshPidBatchAsync(token);
                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusMessage = UserError.Describe(ex); }
    }

    private async Task RefreshPidBatchAsync(CancellationToken token)
    {
        await _pidRefreshGate.WaitAsync(token);
        try
        {
            var all = PidReadings.ToArray();
            if (all.Length == 0) return;

            var batch = all
                .Skip(_pidCursor)
                .Take(PidBatchSize)
                .Concat(all.Take(Math.Max(0, _pidCursor + PidBatchSize - all.Length)))
                .ToArray();
            _pidCursor = (_pidCursor + batch.Length) % all.Length;

            var values = await _obd.ReadLiveValuesAsync(batch.Select(item => item.Pid), token);
            var valuesByPid = values.ToDictionary(item => item.Pid);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                foreach (var item in batch)
                {
                    if (valuesByPid.TryGetValue(item.Pid, out var value)) item.ApplyValue(value.Value, _units);
                    else item.MarkNoResponse();
                }
            });
        }
        finally { _pidRefreshGate.Release(); }
    }

    private void RefreshUnitPresentation()
    {
        foreach (var item in PidReadings)
            item.RefreshPresentation(_units);
    }

    private static bool HasKnownValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(value, "მიუწვდომელია", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(value, "—", StringComparison.OrdinalIgnoreCase);

    private static string FirstKnownValue(params string?[] values) =>
        values.FirstOrDefault(HasKnownValue) ?? "მიუწვდომელია";

    private static string DisplayValue(string? value) => HasKnownValue(value) ? value! : "მიუწვდომელია";

    private static string DisplayVehicleName(VehicleInfo? vehicle)
    {
        var name = string.Join(" ", new[] { vehicle?.Manufacturer, vehicle?.Model }.Where(HasKnownValue));
        var year = DisplayValue(vehicle?.Year);
        return string.IsNullOrWhiteSpace(name) ? $"Vehicle: მიუწვდომელია · {year}" : $"{name} · {year}";
    }
}
public partial class ProViewModel : BaseViewModel
{
    private readonly ProService _pro;
    private readonly PurchaseService _purchase;
    private readonly LocalizationService _localization;
    private readonly CarScanner.VinCheck.Services.IVinHistoryService _vin;
    private readonly AccountService _account;

    private Dictionary<string, string> _prices = new();
    private CarScanner.VinCheck.Models.VinQuotaInfo? _quota;

    public ObservableCollection<ProPlanItem> Plans { get; } = new();
    public ObservableCollection<ProPackItem> Packs { get; } = new();

    [ObservableProperty]
    private bool isPro;

    [ObservableProperty]
    private bool hasVin;

    [ObservableProperty]
    private string status = "";

    [ObservableProperty]
    private Color statusColor =
        Color.FromArgb("#94A3B8");

    [ObservableProperty]
    private string priceText = "";

    // ============================================================
    // LOCALIZED UI TEXT
    // ============================================================

    [ObservableProperty]
    private string pageTitleText = "";

    [ObservableProperty]
    private string proSubtitleText = "";

    [ObservableProperty]
    private string perMonthText = "";

    [ObservableProperty]
    private string activateProText = "";

    [ObservableProperty]
    private string restorePurchaseText = "";

    [ObservableProperty]
    private string subscriptionInfoText = "";

    [ObservableProperty]
    private string privacyPolicyText = "";

    [ObservableProperty]
    private string termsOfUseText = "";

    [ObservableProperty]
    private string manageSubscriptionText = "";

    [ObservableProperty]
    private string packsTitleText = "";

    [ObservableProperty]
    private string packsHintText = "";

    [ObservableProperty]
    private string vinQuotaText = "";

    [ObservableProperty]
    private string currentPlanTitleText = "";

    [ObservableProperty]
    private string currentPlanText = "";

    [ObservableProperty]
    private string plansTitleText = "";

    public ProViewModel(
        ProService pro,
        PurchaseService purchase,
        LocalizationService localization,
        CarScanner.VinCheck.Services.IVinHistoryService vin,
        AccountService account)
    {
        _account = account;
        _pro = pro;
        _purchase = purchase;
        _localization = localization;
        _vin = vin;

        IsPro = pro.IsPro;
        HasVin = pro.HasVin;

        Plans.Add(new ProPlanItem { ProductId = ProService.ObdProductId });
        Plans.Add(new ProPlanItem { ProductId = ProService.VinProductId });
        Plans.Add(new ProPlanItem { ProductId = ProService.FullProductId, IsRecommended = true });
        Plans.Add(new ProPlanItem { ProductId = ProPlanItem.FreeId });
        Packs.Add(new ProPackItem { ProductId = ProService.VinPack5ProductId, Credits = 5 });
        Packs.Add(new ProPackItem { ProductId = ProService.VinPack10ProductId, Credits = 10 });

        Attach();

        ApplyLanguageTexts();

        _ = LoadStoreMetadataAsync();
    }

    // Detached by ProPage when it goes away (no leak through singleton services).
    private bool _attached;

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _pro.ProChanged += OnProChanged;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _pro.ProChanged -= OnProChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnProChanged(object? sender, bool isPro) =>
        MainThread.BeginInvokeOnMainThread(RefreshAccess);

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(ApplyLanguageTexts);

    private string L(string ka, string en) =>
        _localization.IsEnglish ? en : ka;

    /// <summary>
    /// In-app purchase is offered on iPhone (App Store). On Android the app is
    /// consumption-only: PRO bought outside the app is unlocked by signing in
    /// (Google Play merchant accounts are not available from Georgia). Per
    /// Google Play policy the app must not point to other ways to pay.
    /// </summary>
    public bool CanBuyInApp => DeviceInfo.Current.Platform == DevicePlatform.iOS;

    [ObservableProperty]
    private bool showPacks;

    private string StoreName => DeviceInfo.Current.Platform == DevicePlatform.iOS ? "App Store" : "Google Play";

    // ============================================================
    // LANGUAGE
    // ============================================================

    private void ApplyLanguageTexts()
    {
        PageTitleText = "GCar360 PRO";

        ProSubtitleText = L(
            "PRO გეგმა მიბმულია შენს GCar360 ანგარიშზე",
            "PRO plans are linked to your GCar360 account");

        CurrentPlanTitleText = L("შენი გეგმა", "Your plan");
        PlansTitleText = L("რას მოიცავს", "What is included");

        PerMonthText = L(
            "თვეში",
            "per month");

        ActivateProText = L(
            "PRO-ის გააქტიურება",
            "Activate PRO");

        RestorePurchaseText = CanBuyInApp
            ? L("შესყიდვების აღდგენა", "Restore Purchases")
            : L("PRO-ს აღდგენა", "Restore PRO");

        SubscriptionInfoText = (CanBuyInApp
            ? L("გამოწერა ყოველთვე ავტომატურად გრძელდება, სანამ App Store-ში არ გააუქმებ. ", "The subscription renews every month until you cancel it in the App Store. ")
            : string.Empty) + L(
            "PRO გეგმა და დამატებითი VIN შემოწმებები ინახება შენს ანგარიშზე და ჩაირთვება ყველა მოწყობილობაზე, სადაც ამ ანგარიშით შეხვალ.",
            "PRO plans and extra VIN checks are stored on your account and work on every device where you sign in with it.");

        PrivacyPolicyText = L(
            "კონფიდენციალურობის პოლიტიკა",
            "Privacy Policy");

        TermsOfUseText = L(
            "გამოყენების პირობები",
            "Terms of Use");

        ManageSubscriptionText = L(
            "გამოწერის მართვა",
            "Manage Subscription");

        PacksTitleText = L(
            "დამატებითი VIN შემოწმებები",
            "Extra VIN checks");

        PacksHintText = L(
            "ერთჯერადი შეძენა · ვადა არ გასდის",
            "One-time purchase · never expires");

        Title = PageTitleText;

        RefreshPlans();
    }

    private string Price(string productId, decimal fallback) =>
        _prices.TryGetValue(productId, out var price) && !string.IsNullOrWhiteSpace(price)
            ? price
            : $"{fallback.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} ₾";

    private void RefreshPriceText()
    {
        PriceText = $"{Price(ProService.ObdProductId, ProService.PriceGEL)} / {L("თვე", "month")}";
    }

    private void RefreshAccess()
    {
        IsPro = _pro.HasObd;
        HasVin = _pro.HasVin;
        RefreshPlans();
    }

    private void RefreshPlans()
    {
        RefreshPriceText();
        var month = L("თვე", "month");
        var hasObd = _pro.HasObd;
        var hasVin = _pro.HasVin;

        var hasAny = hasObd || hasVin;
        foreach (var plan in Plans)
        {
            switch (plan.ProductId)
            {
                case ProPlanItem.FreeId:
                    plan.Title = L("უფასო", "Free");
                    plan.Subtitle = string.Empty;
                    plan.PriceText = string.Empty;
                    plan.Features = L(
                        "✓ შეცდომის კოდები და მათი ახსნა\n✓ ცოცხალი მონაცემები\n✓ საწვავის ხარჯი, სერვისი, 0–100\n✓ VIN: მანქანის მონაცემები და ისტორიის მიმოხილვა",
                        "✓ Fault codes with explanations\n✓ Live data\n✓ Fuel use, service, 0–100\n✓ VIN: car details and a history preview");
                    plan.IsActive = !hasAny;
                    break;
                case ProService.ObdProductId:
                    plan.Title = "PRO";
                    plan.Subtitle = L("მანქანის სრული დიაგნოსტიკა", "Full car diagnostics");
                    plan.PriceText = $"{Price(plan.ProductId, ProService.PriceGEL)} / {month}";
                    plan.Features = L(
                        "✓ ყველა მოდულის სკანირება (ABS, SRS, ტრანსმისია…)\n✓ მოდულების ცოცხალი მონაცემები და გრაფიკები\n✓ ყიდვისწინა შემოწმება\n✓ AI დიაგნოსტიკა და კონსულტაცია",
                        "✓ Scan every module (ABS, SRS, transmission…)\n✓ Module live data and graphs\n✓ Pre-purchase check\n✓ AI diagnostics and consultation");
                    plan.IsActive = hasObd;
                    break;

                case ProService.VinProductId:
                    plan.Title = "VIN PRO";
                    plan.Subtitle = L("მანქანის ისტორია VIN კოდით", "Car history by VIN");
                    plan.PriceText = $"{Price(plan.ProductId, ProService.VinPriceGEL)} / {month}";
                    plan.Features = L(
                        $"✓ VIN: სრული ისტორია, {ProService.VinMonthlyReports} ყოველთვე\n✓ აუქციონის ფოტოები და დაზიანებები\n✓ გარბენი და ტიტულის სტატუსი\n✓ უკვე გახსნილი VIN თავიდან უფასოდ იხსნება",
                        $"✓ VIN: full history, {ProService.VinMonthlyReports} every month\n✓ Auction photos and damage\n✓ Mileage and title status\n✓ An opened VIN reopens for free");
                    plan.IsActive = hasVin;
                    break;

                default:
                    plan.Title = L("სრული PRO", "Full PRO");
                    plan.Subtitle = L("OBD PRO + VIN PRO ერთად", "OBD PRO + VIN PRO together");
                    plan.PriceText = $"{Price(plan.ProductId, ProService.FullPriceGEL)} / {month}";
                    plan.Features = L(
                        "✓ ყველაფერი PRO-დან\n✓ ყველაფერი VIN PRO-დან\n✓ ყველაზე მომგებიანი",
                        "✓ Everything in PRO\n✓ Everything in VIN PRO\n✓ The best value");
                    plan.IsActive = hasObd && hasVin;
                    plan.BadgeText = L("საუკეთესო", "Best value");
                    break;
            }

            // Card: "current" (green) for the plan you have, "best value" (blue) for Full PRO.
            plan.ShowPrice = CanBuyInApp && !plan.IsFree;
            plan.CanOpen = !plan.IsActive && !plan.IsFree;
            plan.TagText = plan.IsActive
                ? L("მიმდინარე", "Current")
                : plan.IsRecommended ? L("საუკეთესო", "Best value") : string.Empty;
            plan.TagBackground = Color.FromArgb(plan.IsActive ? "#E8F5EE" : "#2563EB");
            plan.TagTextColor = Color.FromArgb(plan.IsActive ? "#1F8A4C" : "#FFFFFF");
            plan.StrokeColor = Color.FromArgb(plan.IsActive ? "#22C55E" : plan.IsRecommended ? "#3B82F6" : "#6694A3B8");
        }

        // Android (Google Play payments policy): ShowPrice above is false and
        // packs stay hidden, so no price is shown and nothing points to another
        // way to pay; only "Sign in" and "Restore" unlock PRO already on the account.
        foreach (var pack in Packs)
        {
            var fallback = pack.Credits == 5 ? ProService.VinPack5PriceGEL : ProService.VinPack10PriceGEL;
            pack.Title = $"+{pack.Credits} VIN";
            pack.PriceText = Price(pack.ProductId, fallback);
            pack.ButtonText = $"+{pack.Credits} VIN — {pack.PriceText}";
        }

        ShowPacks = CanBuyInApp && hasVin;

        string Until(DateTime? date) =>
            date is { } d && d.Year < 2100 ? L($" · {d.ToLocalTime():dd.MM.yyyy}-მდე", $" · until {d.ToLocalTime():dd.MM.yyyy}") : string.Empty;

        CurrentPlanText = _pro.Tier switch
        {
            "full" => L("სრული PRO", "Full PRO") + Until(_pro.ExpiresAt),
            "obd" => "OBD PRO" + Until(_pro.ExpiresAt),
            "vin" => "VIN PRO" + Until(_pro.VinExpiresAt),
            _ => L("უფასო ვერსია", "Free version")
        };

        VinQuotaText = _quota is null || (!hasVin && _quota.Credits <= 0)
            ? string.Empty
            : !hasVin
                ? L($"დამატებითი VIN შემოწმებები: {_quota.Credits}", $"Extra VIN checks: {_quota.Credits}")
            : _quota.Credits > 0
                ? L($"დარჩენილია ამ თვეში: {_quota.Remaining} / {_quota.MonthlyLimit} · დამატებითი: {_quota.Credits}",
                    $"Left this month: {_quota.Remaining} / {_quota.MonthlyLimit} · extra: {_quota.Credits}")
                : L($"დარჩენილია ამ თვეში: {_quota.Remaining} / {_quota.MonthlyLimit}",
                    $"Left this month: {_quota.Remaining} / {_quota.MonthlyLimit}");
    }

    // ============================================================
    // STORE PRICE
    // ============================================================

    private async Task LoadStoreMetadataAsync()
    {
        try
        {
            await _pro.LoadAsync(forceServerRefresh: true);
            _quota = await _vin.GetQuotaAsync(CancellationToken.None);
            MainThread.BeginInvokeOnMainThread(RefreshAccess);
        }
        catch
        {
            // Store metadata ვერ ჩაიტვირთა.
            // გვერდი მაინც ჩვეულებრივ გაიხსნება.
        }
    }

    // ============================================================
    // PURCHASE
    // ============================================================

    [RelayCommand]
    public Task Purchase() =>
        BuyPlan(Plans.First(plan => plan.ProductId == ProService.ObdProductId));

    [RelayCommand]
    public async Task BuyPlan(ProPlanItem? plan)
    {
        try
        {
        if (plan is null || IsBusy)
            return;

        if (plan.IsActive)
        {
            Status = L(
                "ეს გეგმა უკვე აქტიურია.",
                "This plan is already active.");

            StatusColor =
                Color.FromArgb("#22C55E");

            return;
        }

        try
        {
            IsBusy = true;

            Status = L(
                $"{StoreName} იტვირთება…",
                $"Opening {StoreName}…");

            StatusColor =
                Color.FromArgb("#94A3B8");

            var result =
                await _purchase.BeginSubscriptionPurchaseAsync(plan.ProductId);

            (Status, StatusColor) =
                PurchaseStatusPresentation(result);

            if (result == "SUCCESS" ||
                result == "PRO_ALREADY_ACTIVE")
            {
                await _pro.LoadAsync(
                    forceServerRefresh: true);
                if (_pro.HasVin)
                    _quota = await _vin.GetQuotaAsync(CancellationToken.None);
            }

            RefreshAccess();
        }
        catch (Exception)
        {
            Status = L(
                "გადახდა ვერ შესრულდა. სცადე ხელახლა.",
                "Payment could not be completed. Please try again.");

            StatusColor =
                Color.FromArgb("#DC2626");
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ProViewModel.BuyPlan");
        }
    }

    /// <summary>
    /// A plan card was tapped: iPhone buys it in the App Store; Android opens
    /// sign-in (PRO bought outside the app is linked to the account).
    /// </summary>
    [RelayCommand]
    public async Task OpenPlan(ProPlanItem? plan)
    {
        try
        {
        if (plan is null || plan.IsFree || IsBusy)
            return;

        if (plan.IsActive)
        {
            Status = L("ეს შენი მიმდინარე გეგმაა.", "This is your current plan.");
            StatusColor = Color.FromArgb("#22C55E");
            return;
        }

        if (CanBuyInApp)
        {
            await BuyPlan(plan);
            return;
        }

        if (!_account.IsSignedIn)
        {
            await Shell.Current.GoToAsync(nameof(Views.AccountPage));
            return;
        }

        await Restore();
        if (!plan.IsActive)
        {
            Status = L(
                $"{plan.Title} შენს ანგარიშზე ({_account.Email}) ჯერ არ არის აქტიური.",
                $"{plan.Title} is not active on your account ({_account.Email}) yet.");
            StatusColor = Color.FromArgb("#B45309");
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ProViewModel.OpenPlan");
        }
    }

    [RelayCommand]
    public async Task BuyPack(ProPackItem? pack)
    {
        try
        {
        if (pack is null || IsBusy)
            return;

        if (!_pro.HasVin)
        {
            Status = L(
                "VIN პაკეტები ხელმისაწვდომია VIN PRO ან სრული PRO გამოწერით.",
                "VIN packs are available with VIN PRO or Full PRO.");
            StatusColor = Color.FromArgb("#B45309");
            return;
        }

        try
        {
            IsBusy = true;
            Status = L($"{StoreName} იტვირთება…", $"Opening {StoreName}…");
            StatusColor = Color.FromArgb("#94A3B8");

            var result = await _purchase.BuyVinCreditsAsync(pack.ProductId);
            if (result == "SUCCESS")
            {
                Status = L($"+{pack.Credits} VIN დაემატა.", $"+{pack.Credits} VIN added.");
                StatusColor = Color.FromArgb("#22C55E");
                _quota = await _vin.GetQuotaAsync(CancellationToken.None);
                RefreshPlans();
            }
            else
            {
                (Status, StatusColor) = PurchaseStatusPresentation(result);
            }
        }
        catch (Exception)
        {
            Status = L(
                "გადახდა ვერ შესრულდა. სცადე ხელახლა.",
                "Payment could not be completed. Please try again.");
            StatusColor = Color.FromArgb("#DC2626");
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ProViewModel.BuyPack");
        }
    }

    // ============================================================
    // RESTORE PURCHASE
    // ============================================================

    [RelayCommand]
    public async Task Restore()
    {
        try
        {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;

            Status = L(
                "სტატუსი მოწმდება…",
                "Checking your plan…");

            StatusColor =
                Color.FromArgb("#94A3B8");

            // App Store requires a real "Restore Purchases" for in-app subscriptions.
            if (CanBuyInApp)
            {
                try
                {
                    await _purchase.RestorePurchasesAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"App Store restore failed: {ex.GetType().Name}");
                }
            }

            await _pro.LoadAsync(
                forceServerRefresh: true);

            _quota = await _vin.GetQuotaAsync(CancellationToken.None);

            await LoadWebSubscriptionAsync();

            RefreshAccess();

            if (_pro.HasObd || _pro.HasVin)
            {
                Status = L(
                    "გეგმა აქტიურია.",
                    "Your plan is active.");

                StatusColor =
                    Color.FromArgb("#22C55E");
            }
            else
            {
                Status = L(
                    "აქტიური გეგმა ვერ მოიძებნა. დარწმუნდი, რომ სწორი ანგარიშით ხარ შესული.",
                    "No active plan was found. Make sure you are signed in with the right account.");

                StatusColor =
                    Color.FromArgb("#B45309");
            }
        }
        catch (Exception)
        {
            Status = L(
                "სტატუსი ვერ შემოწმდა. შეამოწმე ინტერნეტი და სცადე ხელახლა.",
                "Could not check your plan. Check your connection and try again.");

            StatusColor =
                Color.FromArgb("#DC2626");
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ProViewModel.Restore");
        }
    }

    // ============================================================
    // LINKS
    // ============================================================

    [RelayCommand]
    private Task PrivacyPolicy() =>
        StoreLinks.OpenAsync(
            StoreLinks.PrivacyPolicyUrl);

    [RelayCommand]
    private Task TermsOfUse() =>
        StoreLinks.OpenAsync(
            StoreLinks.TermsOfUseUrl);

    [RelayCommand]
    private Task ManageSubscription() =>
        StoreLinks.OpenAsync(
            StoreLinks.ManageSubscriptionUrl);

    // ============================================================
    // PURCHASE RESULT PRESENTATION
    // ============================================================

    private (string Message, Color Color)
        PurchaseStatusPresentation(
            string result)
    {
        var success =
            Color.FromArgb("#22C55E");

        var warning =
            Color.FromArgb("#B45309");

        var error =
            Color.FromArgb("#DC2626");

        return result switch
        {
            "SUCCESS" =>
                (
                    L(
                        "გამოწერა გააქტიურდა. ის ყოველთვიურად განახლდება გაუქმებამდე.",
                        "Your plan is active. It renews monthly until cancelled."),
                    success
                ),

            "PRO_ALREADY_ACTIVE" =>
                (
                    L(
                        "ეს გეგმა უკვე აქტიურია.",
                        "This plan is already active."),
                    success
                ),

            CarScanner.Services.PurchaseService.PurchasesNotAvailableOnAndroid =>
                (
                    CarScanner.Services.PurchaseService.PurchasesNotAvailableOnAndroidMessage,
                    warning
                ),

            "PENDING" =>
                (
                    L(
                        "გადახდა მოლოდინშია. დადასტურების შემდეგ ავტომატურად გააქტიურდება.",
                        "Payment is pending. It will activate after confirmation."),
                    warning
                ),

            "CANCELLED"
            or "ERROR:CANCELLED_BY_APP"
            or "ERROR:PURCHASE_CANCELLED" =>
                (
                    L(
                        "გადახდა გაუქმდა და თანხა არ ჩამოგეჭრა.",
                        "Payment was cancelled and you were not charged."),
                    warning
                ),

            "STORE_UNAVAILABLE"
            or "ERROR:SERVICE_UNAVAILABLE"
            or "ERROR:BILLING_UNAVAILABLE"
            or "ERROR:NETWORK_ERROR" =>
                (
                    L(
                        $"{StoreName} დროებით მიუწვდომელია. შეამოწმე ინტერნეტი და სცადე ხელახლა.",
                        $"{StoreName} is temporarily unavailable. Check your connection and try again."),
                    error
                ),

            "ERROR:ITEM_ALREADY_OWNED"
            or "ERROR:OWNED_SUBSCRIPTION_NOT_FOUND"
            or "ERROR:OWNED_PURCHASE_NOT_FOUND" =>
                (
                    L(
                        "ეს უკვე შეძენილია. დააჭირე „შეძენის აღდგენას“.",
                        "This is already purchased. Tap Restore Purchase."),
                    warning
                ),

            "ERROR:PRODUCT_NOT_FOUND"
            or "ERROR:ITEM_UNAVAILABLE"
            or "ERROR:SUBSCRIPTION_OFFER_NOT_FOUND"
            or "ERROR:DEVELOPER_ERROR" =>
                (
                    L(
                        "ეს შეთავაზება Google Play-ში ჯერ ხელმისაწვდომი არ არის.",
                        "This offer is not available in Google Play yet."),
                    error
                ),

            "ERROR:SERVER_VERIFICATION_FAILED"
            or "ERROR:ENTITLEMENT_NOT_ACTIVE" =>
                (
                    L(
                        "გადახდის დადასტურება ვერ დასრულდა. თანხა მეორედ არ გადაიხადო — ცოტა ხანში სცადე შეძენის აღდგენა.",
                        "Payment verification could not be completed. Do not pay again — try Restore Purchase shortly."),
                    error
                ),

            _ =>
                (
                    L(
                        "გადახდა ვერ შესრულდა. სცადე ხელახლა.",
                        "Payment could not be completed. Please try again."),
                    error
                )
        };
    }
}

/// <summary>One subscription card on the PRO page.</summary>
public sealed partial class ProPlanItem : ObservableObject
{
    public const string FreeId = "free";

    public string ProductId { get; init; } = "";
    public bool IsFree => ProductId == FreeId;
    public bool IsRecommended { get; init; }
    public bool HasBadge => IsRecommended;

    [ObservableProperty] private string title = "";
    [ObservableProperty] private string subtitle = "";
    [ObservableProperty] private string priceText = "";
    [ObservableProperty] private string features = "";
    [ObservableProperty] private string buttonText = "";
    [ObservableProperty] private string badgeText = "";
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private bool showPrice;
    [ObservableProperty] private bool canOpen;
    [ObservableProperty] private string tagText = "";
    [ObservableProperty] private Color tagBackground = Color.FromArgb("#2563EB");
    [ObservableProperty] private Color tagTextColor = Colors.White;

    public bool HasTag => !string.IsNullOrEmpty(TagText);
    partial void OnTagTextChanged(string value) => OnPropertyChanged(nameof(HasTag));
    [ObservableProperty] private Color strokeColor = Color.FromArgb("#6694A3B8");

    public bool CanBuy => !IsActive;
    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(CanBuy));
}

/// <summary>One VIN credit pack on the PRO page.</summary>
public sealed partial class ProPackItem : ObservableObject
{
    public string ProductId { get; init; } = "";
    public int Credits { get; init; }

    [ObservableProperty] private string title = "";
    [ObservableProperty] private string priceText = "";
    [ObservableProperty] private string buttonText = "";
}
public partial class ServiceViewModel : BaseViewModel
{
    private readonly OBDService _obd;
    private readonly VehicleInfoService _vehicle;
    private readonly VehicleProfileResolverService _vehicleProfiles;
    private readonly OemOperationService _oemOperations;
    private readonly OemCodingBackupService _codingBackups;
    private readonly LocalizationService _localization;
    private readonly ServiceProcedureCatalogService _serviceProcedureCatalog;
    private readonly ServiceDataReaderService _serviceDataReader;
    private readonly ServiceResetVerificationService _serviceResetVerifier;
    private readonly OemDataCaptureService _oemDataCapture;
    private readonly VehicleBrandProfileService _brandProfiles;
    private readonly MaintenanceCalendarService _maintenanceCalendar;
    private VehicleInfo? _currentOemVehicle;
    private CancellationTokenSource? _activeTestCancellation;
    private CancellationTokenSource? _refreshCancellation;
    private IReadOnlyList<ServiceMetricReading> _lastServiceReadings = Array.Empty<ServiceMetricReading>();
    private string _lastServiceReadDetail = string.Empty;
    private CancellationTokenSource? _discoveryCancellation;

    // ---- GM oil-life discovery (read-only) ----
    [ObservableProperty] private bool showOilDiscovery;
    [ObservableProperty] private bool isDiscoveryRunning;
    [ObservableProperty] private string dashboardOilPercentText = "";
    [ObservableProperty] private string discoveryProgressText = "";
    [ObservableProperty] private string discoveryReport = "";
    [ObservableProperty] private string discoveryTitleText = "";
    [ObservableProperty] private string discoveryHintText = "";
    [ObservableProperty] private string discoveryQuickButtonText = "";
    [ObservableProperty] private string discoveryDeepButtonText = "";
    [ObservableProperty] private string discoveryPlaceholderText = "";
    public bool IsDiscoveryIdle => !IsDiscoveryRunning;
    public bool HasDiscoveryReport => !string.IsNullOrWhiteSpace(DiscoveryReport);
    partial void OnIsDiscoveryRunningChanged(bool value) => OnPropertyChanged(nameof(IsDiscoveryIdle));
    partial void OnDiscoveryReportChanged(string value)
    {
        OnPropertyChanged(nameof(HasDiscoveryReport));
        DiscoveryResultText = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Contains("★", StringComparison.Ordinal)
                ? L("ნაპოვნია და შენახულია — ზეთის რესურსი და განულება ახლა გამოჩნდება.", "Found and saved — oil life and reset now appear.")
                : value.Contains("No identifier matched", StringComparison.Ordinal)
                    ? L("ეკრანის % ვერცერთ მნიშვნელობას არ დაემთხვა. შეამოწმე, რომ ზუსტად ის % ჩაწერე, რასაც მანქანის ეკრანი ახლა აჩვენებს, და ანთება ჩართულია.",
                        "No value matched the dashboard %. Check that you entered exactly the % the car shows now and that the ignition is on.")
                    : value.StartsWith("GCar360", StringComparison.Ordinal)
                        ? L("ძიება დასრულდა, შესაბამისობა ვერ მოიძებნა.", "Search finished without a match.")
                        : value;
    }

    [ObservableProperty] private string discoveryResultText = "";
    public bool HasDiscoveryResult => !string.IsNullOrWhiteSpace(DiscoveryResultText);
    partial void OnDiscoveryResultTextChanged(string value) => OnPropertyChanged(nameof(HasDiscoveryResult));
    public string DiscoveryCancelText => L("შეჩერება", "Stop");
    public string DiscoveryCopyText => L("ტექნიკური ანგარიშის კოპირება", "Copy technical report");

    private static readonly HashSet<string> GmBrandKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "chevrolet", "gmc", "buick", "cadillac", "opel", "vauxhall", "holden", "pontiac", "saturn", "hummer"
    };

    private bool IsGmVehicle(VehicleInfo? vehicle)
    {
        static string Key(string? v) => new string((v ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return GmBrandKeys.Contains(Key(vehicle?.Manufacturer)) ||
               GmBrandKeys.Contains(Key(_brandProfiles.Current.Id)) ||
               GmBrandKeys.Contains(Key(_brandProfiles.Current.DisplayName));
    }

    [RelayCommand]
    private Task RunQuickOilDiscovery() => RunOilDiscoveryAsync(deepScan: false);

    [RelayCommand]
    private Task RunDeepOilDiscovery() => RunOilDiscoveryAsync(deepScan: true);

    [RelayCommand]
    private void CancelOilDiscovery() => _discoveryCancellation?.Cancel();

    [RelayCommand]
    private async Task OpenResetRecorder()
    {
        try { await Shell.Current.GoToAsync(nameof(Views.ResetRecorderPage)); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Reset recorder navigation failed: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task CopyDiscoveryReport()
    {
        try
        {
        if (string.IsNullOrWhiteSpace(DiscoveryReport))
            return;
        try { await Clipboard.Default.SetTextAsync(DiscoveryReport); } catch { }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ServiceViewModel.CopyDiscoveryReport");
        }
    }

    public ObservableCollection<ServiceSearchHitRow> SearchHits { get; } = new();
    [ObservableProperty] private bool hasSearchHits;

    private static ServiceSearchReportService? SearchReporter =>
        Application.Current?.Handler?.MauiContext?.Services.GetService<ServiceSearchReportService>();

    private string _knownSourcesCheckedFor = string.Empty;
    private ServiceSearchResult? _lastSearch;
    private IReadOnlyList<ServiceSearchHit> _lastSearchHits = Array.Empty<ServiceSearchHit>();
    private VehicleInfo? _lastSearchVehicle;
    private double? _lastSearchPercent;

    /// <summary>
    /// The user confirms that a value of the search (e.g. "Distance to next
    /// service 4 200 km") is what the dashboard shows. It is remembered for
    /// this car, shared as confirmed and shown on the Service page.
    /// </summary>
    public async Task ConfirmSearchHitAsync(ServiceSearchHitRow row)
    {
        if (!row.CanConfirm || _lastSearchVehicle is not { } vehicle || _lastSearch is not { } search)
            return;

        _serviceDataReader.SavePreferredSources(vehicle, new[] { row.SourceKey });
        _lastSearchHits = _lastSearchHits
            .Select(hit => hit.SourceKey == row.SourceKey ? hit with { MatchesDashboard = true } : hit)
            .ToArray();

        var index = SearchHits.IndexOf(row);
        if (index >= 0)
            SearchHits[index] = row with { Mark = "✓", MarkColor = "#1F8A4C", Detail = L("დადასტურდა — შენახულია", "Confirmed — saved"), CanConfirm = false };

        if (SearchReporter is { } reporter)
            _ = reporter.ReportAsync(vehicle, search, _lastSearchHits, _lastSearchPercent);

        if (!IsBusy)
            await Refresh();
    }

    /// <summary>
    /// No oil value on this car yet: ask the server whether other drivers
    /// already confirmed a source for the same make/model/year (no search needed).
    /// </summary>
    private async Task TryKnownSourcesAsync(VehicleInfo? vehicle, CancellationToken token)
    {
        if (vehicle is null || HasOilResetCard || SearchReporter is not { } reporter)
            return;

        var key = $"{vehicle.Manufacturer}|{vehicle.Model}|{vehicle.Year}";
        if (key == _knownSourcesCheckedFor)
            return;
        _knownSourcesCheckedFor = key;

        var known = await reporter.KnownSourcesAsync(vehicle, token);
        if (known.Count == 0)
            return;

        _serviceDataReader.SavePreferredSources(vehicle, known);
        await RefreshServiceValuesAsync(vehicle, token);
    }

    /// <summary>
    /// "Search": tries every known service identifier (all makes, read-only)
    /// without stopping at the first answer, compares the oil values with the
    /// dashboard %, remembers the matching source for this car and shares the
    /// result. GM cars then also get the GMLAN identifier scan.
    /// </summary>
    private async Task RunOilDiscoveryAsync(bool deepScan)
    {
        if (IsBusy || IsDiscoveryRunning)
            return;

        double? percent = int.TryParse(DashboardOilPercentText?.Trim().TrimEnd('%'), out var p) && p is >= 0 and <= 100
            ? p
            : null;

        _discoveryCancellation?.Dispose();
        _discoveryCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(25));
        var token = _discoveryCancellation.Token;
        var vehicle = _currentOemVehicle ?? _vehicle.CurrentVehicle ?? new VehicleInfo();
        var matchedAny = false;
        var report = new System.Text.StringBuilder();

        try
        {
            IsBusy = true;
            IsDiscoveryRunning = true;
            DiscoveryReport = "";
            SearchHits.Clear();
            HasSearchHits = false;

            if (!_obd.IsInitialized)
                await _obd.InitializeAsync(token);

            // 1) Every catalog source (read-only), no early stop.
            var progress = new System.Progress<string>(step =>
                DiscoveryProgressText = L($"მოწმდება {step}…", $"Checking {step}…"));
            var search = await _serviceDataReader.SearchAllSourcesAsync(vehicle, progress, token);
            var hits = ServiceDataReaderService.MatchDashboard(search.Hits, percent);

            report.AppendLine($"GCar360 service search · {vehicle.Manufacturer} {vehicle.Model} {vehicle.Year}");
            report.AppendLine($"tried {search.RequestsTried} · answered {search.RequestsAnswered} · dashboard {(percent.HasValue ? $"{percent}%" : "—")}");
            foreach (var hit in hits)
            {
                report.AppendLine($"{(hit.MatchesDashboard ? "★" : "·")} {hit.Name} = {hit.Value:0.##} {hit.Unit} · {hit.Request} · {hit.Module} · {hit.CatalogMake}");
                SearchHits.Add(new ServiceSearchHitRow(
                    hit.MatchesDashboard ? "✓" : "•",
                    hit.MatchesDashboard ? "#1F8A4C" : "#5D636B",
                    $"{hit.Name}: {hit.Value:0.##} {hit.Unit}".Trim(),
                    hit.MatchesDashboard
                        ? L("ემთხვევა პანელს", "Matches the dashboard")
                        : L($"თუ პანელზეც ასეა — დააჭირე „სწორია“ · {hit.Module}", $"If the dashboard shows the same — tap to confirm · {hit.Module}"),
                    hit.SourceKey,
                    !hit.MatchesDashboard));
            }
            HasSearchHits = SearchHits.Count > 0;

            _lastSearch = search;
            _lastSearchHits = hits;
            _lastSearchVehicle = vehicle;
            _lastSearchPercent = percent;

            var matched = hits.Where(hit => hit.MatchesDashboard).ToArray();
            if (matched.Length > 0)
            {
                // Remember the confirmed source(s) plus the other values of the same identifier.
                var sameRequest = hits.Where(hit => matched.Any(m => m.Request == hit.Request && m.Module == hit.Module));
                _serviceDataReader.SavePreferredSources(vehicle, sameRequest.Select(hit => hit.SourceKey));
                matchedAny = true;
            }

            if (SearchReporter is { } reporter)
                _ = reporter.ReportAsync(vehicle, search, hits, percent);

            // 2) GM: GMLAN identifier scan when the catalog had no matching value.
            if (!matchedAny && IsGmVehicle(vehicle) && percent is not null)
            {
                var gmProgress = new System.Progress<string>(text => DiscoveryProgressText = text);
                var gm = await _serviceDataReader.RunGmOilLifeDiscoveryAsync((int)percent.Value, deepScan, gmProgress, token);
                report.AppendLine().Append(gm);
                matchedAny = gm.Contains("★", StringComparison.Ordinal);
            }

            DiscoveryReport = report.ToString();
            DiscoveryResultText = matchedAny
                ? L("✓ ნაპოვნია და შენახულია — ზეთის რესურსი ახლა გამოჩნდება.", "✓ Found and saved — oil life now appears.")
                : hits.Count == 0
                    ? L("ვერცერთმა ცნობილმა გზამ ვერ უპასუხა. ამ მანქანისთვის ჯერ წყარო არ გვაქვს — ინსტრუქცია ქვემოთაა.",
                        "None of the known sources answered. We have no source for this car yet — instructions are below.")
                    : percent is null
                        ? L($"უპასუხა {hits.Count} მნიშვნელობამ. ჩაწერე პანელის ზეთის % და გაიმეორე, რომ სწორი ავარჩიოთ.",
                            $"{hits.Count} value(s) answered. Enter the dashboard oil % and repeat to pick the right one.")
                        : L($"უპასუხა {hits.Count} მნიშვნელობამ, მაგრამ არცერთი არ უდრის {percent}%-ს. შეამოწმე პანელის %.",
                            $"{hits.Count} value(s) answered, but none equals {percent}%. Check the dashboard %.");
        }
        catch (OperationCanceledException)
        {
            DiscoveryReport = report.ToString();
            DiscoveryResultText = L("ძიება შეწყდა.", "Search cancelled.");
        }
        catch (Exception ex)
        {
            DiscoveryReport = report.ToString();
            DiscoveryResultText = L($"ძიება ვერ შესრულდა: {UserError.Describe(ex)}", $"Search failed: {UserError.Describe(ex)}");
        }
        finally
        {
            DiscoveryProgressText = "";
            IsDiscoveryRunning = false;
            IsBusy = false;
        }

        // A match was saved → read service values again so it shows up.
        if (matchedAny)
            await Refresh();
    }

    // ---- Reset service reminder (oil life) ----
    [ObservableProperty] private bool hasOilResetCard;
    [ObservableProperty] private bool canResetOilLife;
    [ObservableProperty] private bool isOilResetRunning;
    [ObservableProperty] private string oilLifeValueText = "—";
    [ObservableProperty] private double oilLifeFraction;
    [ObservableProperty] private string oilLifeColor = "#1F8A4C";
    [ObservableProperty] private string oilCompatibilityText = "";
    [ObservableProperty] private string oilResetStatusText = "";
    [ObservableProperty] private string oilResetStatusColor = "#94A3B8";
    [ObservableProperty] private string oilResetDetailText = "";
    [ObservableProperty] private string oilResetHistoryText = "";
    [ObservableProperty] private string oilResetUnavailableText = "";
    [ObservableProperty] private string oilRefreshButtonText = "";
    [ObservableProperty] private string resetFullButtonText = "";
    [ObservableProperty] private string resetHalfButtonText = "";
    [ObservableProperty] private string oilResetHistoryTitleText = "";
    public bool IsOilResetIdle => !IsOilResetRunning;
    public bool CanPressOilReset => CanResetOilLife && !IsOilResetRunning;
    public bool ShowOilResetUnavailable => HasOilResetCard && !CanResetOilLife;
    public bool HasOilResetStatus => !string.IsNullOrWhiteSpace(OilResetStatusText);
    public bool HasOilResetDetail => !string.IsNullOrWhiteSpace(OilResetDetailText);
    public bool HasOilResetHistory => !string.IsNullOrWhiteSpace(OilResetHistoryText);
    partial void OnIsOilResetRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsOilResetIdle));
        OnPropertyChanged(nameof(CanPressOilReset));
    }
    partial void OnCanResetOilLifeChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPressOilReset));
        OnPropertyChanged(nameof(ShowOilResetUnavailable));
    }
    partial void OnHasOilResetCardChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowOilResetUnavailable));
        OnPropertyChanged(nameof(ShowDirectResetsSection));
        OnPropertyChanged(nameof(ShowServiceMessageBox));
    }

    // When the oil reset card is shown, the page shows only that card:
    // no instruction list and no "updated from ECU" message.
    public bool ShowDirectResetsSection => HasDirectResets && !HasOilResetCard;
    public bool ShowServiceMessageBox => HasServiceMessage && !HasOilResetCard;
    partial void OnHasDirectResetsChanged(bool value) => OnPropertyChanged(nameof(ShowDirectResetsSection));
    partial void OnHasServiceMessageChanged(bool value) => OnPropertyChanged(nameof(ShowServiceMessageBox));
    partial void OnOilResetStatusTextChanged(string value) => OnPropertyChanged(nameof(HasOilResetStatus));
    partial void OnOilResetDetailTextChanged(string value) => OnPropertyChanged(nameof(HasOilResetDetail));
    partial void OnOilResetHistoryTextChanged(string value) => OnPropertyChanged(nameof(HasOilResetHistory));

    private void SetOilLifeDisplay(double percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        OilLifeValueText = $"{clamped:0.#}%";
        OilLifeFraction = clamped / 100d;
        OilLifeColor = clamped <= 10 ? "#B3361F" : clamped <= 25 ? "#C98A1A" : "#1F8A4C";
    }

    private void UpdateOilCompatibilityText()
    {
        var car = string.IsNullOrWhiteSpace(VehicleSummary) || VehicleSummary == "—"
            ? L("ეს მანქანა", "this vehicle")
            : VehicleSummary;
        OilCompatibilityText = car;
    }

    private void UpdateOilResetHistoryText()
    {
        var history = ServiceDataReaderService.LoadOilResetHistory();
        OilResetHistoryText = string.Join("\n", history.Take(3).Select(entry =>
            $"{entry.Date:dd.MM.yyyy HH:mm} · {entry.Before:0.#}% → {entry.After:0.#}% " +
            (entry.Success ? "✓" : "✕")));
    }

    [RelayCommand]
    private async Task RefreshOilLife()
    {
        try
        {
        if (IsBusy || IsOilResetRunning)
            return;

        if (!_serviceDataReader.CanResetLearnedOilLife)
        {
            await Refresh();
            return;
        }

        try
        {
            IsOilResetRunning = true;
            OilResetStatusText = L("ზეთის რესურსი იკითხება…", "Reading oil life…");
            OilResetStatusColor = "#1D5BD6";
            OilResetDetailText = "";

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if (!_obd.IsInitialized)
                await _obd.InitializeAsync(cancellation.Token);

            var percent = await _serviceDataReader.ReadLearnedOilLifePercentAsync(cancellation.Token);
            if (percent.HasValue)
            {
                SetOilLifeDisplay(percent.Value);
                OilResetStatusText = "";
            }
            else
            {
                OilResetStatusText = L("ECU-მ ზეთის რესურსი ახლა არ დააბრუნა. ანთება ჩართულია?", "The ECU did not return the oil life now. Is the ignition on?");
                OilResetStatusColor = "#C98A1A";
            }
        }
        catch (Exception ex)
        {
            OilResetStatusText = L($"წაკითხვა ვერ მოხერხდა: {UserError.Describe(ex)}", $"Read failed: {UserError.Describe(ex)}");
            OilResetStatusColor = "#B3361F";
        }
        finally
        {
            IsOilResetRunning = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ServiceViewModel.RefreshOilLife");
        }
    }

    [RelayCommand]
    private Task ResetOilLifeFull() => ResetOilLifeAsync(100);

    [RelayCommand]
    private Task ResetOilLifeHalf() => ResetOilLifeAsync(50);

    private async Task ResetOilLifeAsync(int targetPercent)
    {
        if (IsBusy || IsOilResetRunning || !CanResetOilLife)
            return;

        bool confirmed;
        try
        {
            var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null)
                return;

            confirmed = await page.DisplayAlertAsync(
                targetPercent switch { 100 => ResetFullButtonText, 50 => ResetHalfButtonText, _ => $"{targetPercent}%" },
                L(
                    $"ზეთის რესურსი ({OilLifeValueText}) დაყენდება {targetPercent}%-ზე.\n\n" +
                    "• გააკეთე მხოლოდ ზეთის შეცვლის შემდეგ.\n" +
                    "• მანქანა P-ზე, ანთება ჩართული, არ იმოძრაო.\n" +
                    "• აპი ECU-ში ჩაწერს მხოლოდ ერთ მნიშვნელობას და მერე თავიდან წაიკითხავს.\n\nგაგრძელდეს?",
                    $"Oil life ({OilLifeValueText}) will be set to {targetPercent}%.\n\n" +
                    "• Do this only after an oil change.\n" +
                    "• Car in P, ignition on, do not drive.\n" +
                    "• The app writes one value to the ECU and then reads it back.\n\nContinue?"),
                L("განულება", "Reset"),
                L("გაუქმება", "Cancel"));
        }
        catch
        {
            return;
        }

        if (!confirmed)
            return;

        try
        {
            IsOilResetRunning = true;
            OilResetStatusColor = "#1D5BD6";
            OilResetDetailText = "";

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            if (!_obd.IsInitialized)
                await _obd.InitializeAsync(cancellation.Token);

            var progress = new System.Progress<string>(step => OilResetStatusText = step switch
            {
                "speed / voltage" => L("სიჩქარე და ძაბვა მოწმდება…", "Checking speed and voltage…"),
                "read" => L("მიმდინარე მნიშვნელობა ინახება…", "Saving the current value…"),
                "module" => L("ზეთის რესურსის მოდული იძებნება…", "Finding the oil-life module…"),
                "write" => L("განულება იგზავნება…", "Sending reset…"),
                "verify" => L("შედეგი მოწმდება…", "Verifying…"),
                _ => step
            });

            var result = _oilLifeReading is { } oilReading && !IsLearnedGmOilReading(oilReading)
                ? await _serviceDataReader.ResetByWriteBackAsync(oilReading, targetPercent, progress, cancellation.Token)
                : await _serviceDataReader.ResetGmOilLifeAsync(targetPercent, progress, cancellation.Token);

            // A car that refuses the reset does not show the buttons again.
            if (_oilLifeReading is { } refusedCheck && !IsLearnedGmOilReading(refusedCheck))
                CanResetOilLife = _serviceDataReader.CanResetByWriteBack(refusedCheck);

            if (result.After.HasValue)
                SetOilLifeDisplay(result.After.Value);

            // Technical details only when something went wrong.
            OilResetDetailText = result.Success ? string.Empty : result.Detail;
            OilResetStatusColor = result.Success ? "#1F8A4C" : result.WriteSent ? "#B3361F" : "#C98A1A";
            OilResetStatusText = result.Success
                ? result.WriteSent
                    ? L($"✓ განულდა: {result.Before:0.#}% → {result.After:0.#}%", $"✓ Reset done: {result.Before:0.#}% → {result.After:0.#}%")
                    : L($"✓ მნიშვნელობა უკვე {targetPercent}%-ია.", $"✓ The value is already {targetPercent}%.")
                : DescribeOilResetFailure(result);
        }
        catch (OperationCanceledException)
        {
            OilResetStatusText = L("დრო ამოიწურა — პასუხი არ მოვიდა.", "Timed out — no reply.");
            OilResetStatusColor = "#C98A1A";
        }
        catch (Exception ex)
        {
            OilResetStatusText = L($"განულება ვერ შესრულდა: {UserError.Describe(ex)}", $"Reset failed: {UserError.Describe(ex)}");
            OilResetStatusColor = "#B3361F";
        }
        finally
        {
            IsOilResetRunning = false;
            UpdateOilResetHistoryText();
        }
    }

    private string DescribeOilResetFailure(OilLifeResetResult result) => result.Failure switch
    {
        OilLifeResetFailure.NoLearnedSource => L(
            "ამ მანქანაზე განულების იდენტიფიკატორი ჯერ ნაპოვნი არ არის.",
            "No reset identifier has been found for this car yet."),
        OilLifeResetFailure.VehicleMoving => L(
            "მანქანა მოძრაობს — გაჩერდი და P-ზე დააყენე. არაფერი გაგზავნილა.",
            "The car is moving — stop and select P. Nothing was sent."),
        OilLifeResetFailure.LowVoltage => L(
            "ძაბვა დაბალია — ჩართე READY/ძრავა ან დამტენი. არაფერი გაგზავნილა.",
            "Voltage is low — turn on READY/engine or a charger. Nothing was sent."),
        OilLifeResetFailure.ReadFailed => L(
            "მიმდინარე მნიშვნელობა ვერ წავიკითხე — არაფერი გაგზავნილა. ანთება ჩართულია?",
            "Could not read the current value — nothing was sent. Is the ignition on?"),
        OilLifeResetFailure.ModuleNotFound => L(
            "ვერ დადგინდა რომელი მოდული ინახავს ზეთის რესურსს — ბრძანება არ გაიგზავნა. გამოიყენე ქვემოთ მოცემული ინსტრუქცია.",
            "Could not identify the module that stores oil life — nothing was sent. Use the guide below."),
        OilLifeResetFailure.ModuleAmbiguous => L(
            "ამ იდენტიფიკატორს რამდენიმე მოდული პასუხობს — უსაფრთხოებისთვის ბრძანება არ გაიგზავნა.",
            "Several modules answer this identifier — for safety nothing was sent."),
        OilLifeResetFailure.Rejected => result.NegativeCode switch
        {
            0x22 => L(
                "ECU-მ უარი თქვა (7F 3B 22 — პირობები არ არის შესრულებული). სცადე: ანთება ON, ძრავა/READY გამორთული, მანქანა P-ზე.",
                "ECU refused (7F 3B 22 — conditions not correct). Try: ignition ON, engine/READY off, car in P."),
            0x33 => L(
                "ECU-მ უარი თქვა (7F 3B 33 — დაცულია Security Access-ით). ამ მანქანაზე განულება ხელით, ქვემოთ მოცემული ინსტრუქციით გააკეთე.",
                "ECU refused (7F 3B 33 — protected by Security Access). Reset it manually with the guide below."),
            0x31 => L(
                "ECU-მ უარი თქვა (7F 3B 31 — ეს მნიშვნელობა ჩაწერადი არ არის).",
                "ECU refused (7F 3B 31 — this value is not writable)."),
            0x11 or 0x12 => L(
                $"ECU ამ ჩაწერას არ უჭერს მხარს (7F 3B {result.NegativeCode:X2}).",
                $"The ECU does not support this write (7F 3B {result.NegativeCode:X2})."),
            _ => L(
                $"ECU-მ უარი თქვა (7F 3B {result.NegativeCode:X2}). მნიშვნელობა არ შეცვლილა.",
                $"ECU refused (7F 3B {result.NegativeCode:X2}). The value was not changed.")
        },
        OilLifeResetFailure.NotChanged => L(
            $"ბრძანება გაიგზავნა, მაგრამ ECU-ში მნიშვნელობა არ შეიცვალა ({(result.After.HasValue ? $"{result.After:0.#}%" : "—")}). გამოიყენე ქვემოთ მოცემული ინსტრუქცია.",
            $"The command was sent, but the ECU value did not change ({(result.After.HasValue ? $"{result.After:0.#}%" : "—")}). Use the guide below."),
        OilLifeResetFailure.NotSupported => L(
            "ამ მანქანაზე ზეთის რესურსის განულება ჯერ არ არის შემოწმებული — არაფერი გაგზავნილა. გამოიყენე ქვემოთ მოცემული ინსტრუქცია.",
            "Oil-life reset is not verified for this car yet — nothing was sent. Use the guide below."),
        OilLifeResetFailure.SafetyCheckUnavailable => L(
            "სიჩქარე ან ძაბვა ვერ წავიკითხე — უსაფრთხოებისთვის არაფერი გაგზავნილა.",
            "Speed or voltage could not be read — for safety nothing was sent."),
        _ => L("განულება ვერ შესრულდა.", "Reset failed.")
    };

    public ObservableCollection<ServiceProcedureItem> MaintenanceProcedures { get; } = new();
    public ObservableCollection<ServiceProcedureItem> AdaptationProcedures { get; } = new();
    public ObservableCollection<ServiceProcedureItem> CodingFeatures { get; } = new();
    public ObservableCollection<ServiceProcedureItem> ActiveTests { get; } = new();
    public ObservableCollection<ServiceProcedureItem> GuidedServiceProcedures { get; } = new();
    public ObservableCollection<ServiceMetricDisplayItem> ServiceValues { get; } = new();

    /// <summary>Service reminders shown as rows (oil service, Service B, brake fluid…).</summary>
    public ObservableCollection<ServiceReminderRow> ServiceReminders { get; } = new();
    [ObservableProperty] private bool hasServiceReminders;

    [ObservableProperty] private string serviceResult = "";
    [ObservableProperty] private bool hasServiceMessage;
    [ObservableProperty] private string connectionState = "NOT CHECKED";
    [ObservableProperty] private string connectionStateColor = "#94A3B8";
    [ObservableProperty] private string connectionDetail = "";
    [ObservableProperty] private string adapterSummary = "Adapter: — · Protocol: — · Voltage: —";
    [ObservableProperty] private bool isGenericObdReady;

    [ObservableProperty] private string vehicleSummary = "—";
    [ObservableProperty] private string vehicleVin = "VIN: —";

    [ObservableProperty] private string oemState = "NOT CHECKED";
    [ObservableProperty] private string oemStateColor = "#94A3B8";
    [ObservableProperty] private string oemDetail = "";

    [ObservableProperty] private string voltageState = "—";
    [ObservableProperty] private string voltageStateColor = "#94A3B8";
    [ObservableProperty] private string speedState = "—";
    [ObservableProperty] private string speedStateColor = "#94A3B8";
    [ObservableProperty] private string engineState = "—";
    [ObservableProperty] private string engineStateColor = "#94A3B8";

    [ObservableProperty] private bool isActiveTestRunning;
    [ObservableProperty] private bool canRestoreCodingBackup;
    [ObservableProperty] private string codingBackupState = "—";

    [ObservableProperty] private bool showMaintenanceEmpty = true;
    [ObservableProperty] private bool showAdaptationEmpty = true;
    [ObservableProperty] private bool showCodingEmpty = true;
    [ObservableProperty] private bool showActiveTestsEmpty = true;

    [ObservableProperty] private string maintenanceCountText = "0";
    [ObservableProperty] private string adaptationCountText = "0";
    [ObservableProperty] private string codingCountText = "0";
    [ObservableProperty] private string activeTestCountText = "0";
    [ObservableProperty] private bool showServiceValuesEmpty = true;
    [ObservableProperty] private string serviceValueCountText = "0";
    [ObservableProperty] private string serviceValuesStatusText = "";
    [ObservableProperty] private bool showGuidedServiceEmpty = true;
    [ObservableProperty] private string guidedServiceCountText = "0";

    // Dynamic Service Hub visibility. Unsupported/empty sections are hidden.
    [ObservableProperty] private bool hasServiceValues;
    [ObservableProperty] private bool hasDirectResets;
    [ObservableProperty] private bool hasGuidedService;
    [ObservableProperty] private bool hasAdaptations;
    [ObservableProperty] private bool hasCodingFeatures;
    [ObservableProperty] private bool hasActiveTests;
    [ObservableProperty] private bool hasAnyAdvancedOperations;
    [ObservableProperty] private bool showNoServiceContent = true;
    [ObservableProperty] private string advancedOperationCountText = "0";
    [ObservableProperty] private string serviceOverviewText = "";

    // Localized UI text
    [ObservableProperty] private string pageTitleText = "";
    [ObservableProperty] private string headerTitleText = "";
    [ObservableProperty] private string headerSubtitleText = "";
    [ObservableProperty] private string refreshButtonText = "";
    [ObservableProperty] private string vehicleCardTitleText = "";
    [ObservableProperty] private string safetyTitleText = "";
    [ObservableProperty] private string safetySubtitleText = "";
    [ObservableProperty] private string voltageLabelText = "";
    [ObservableProperty] private string stationaryLabelText = "";
    [ObservableProperty] private string engineLabelText = "";
    [ObservableProperty] private string resultTitleText = "";
    [ObservableProperty] private string maintenanceTitleText = "";
    [ObservableProperty] private string maintenanceSubtitleText = "";
    [ObservableProperty] private string adaptationTitleText = "";
    [ObservableProperty] private string adaptationSubtitleText = "";
    [ObservableProperty] private string codingTitleText = "";
    [ObservableProperty] private string codingSubtitleText = "";
    [ObservableProperty] private string activeTestsTitleText = "";
    [ObservableProperty] private string activeTestsSubtitleText = "";
    [ObservableProperty] private string runServiceButtonText = "";
    [ObservableProperty] private string runAdaptationButtonText = "";
    [ObservableProperty] private string runCodingButtonText = "";
    [ObservableProperty] private string runActiveTestButtonText = "";
    [ObservableProperty] private string stopActiveTestButtonText = "";
    [ObservableProperty] private string restoreBackupButtonText = "";
    [ObservableProperty] private string maintenanceEmptyText = "";
    [ObservableProperty] private string adaptationEmptyText = "";
    [ObservableProperty] private string codingEmptyText = "";
    [ObservableProperty] private string activeTestsEmptyText = "";
    [ObservableProperty] private string safetyNoticeTitleText = "";
    [ObservableProperty] private string safetyNoticeText = "";

    [ObservableProperty] private string serviceReminderTitleText = "";
    [ObservableProperty] private string compatibilityConfirmedText = "";
    [ObservableProperty] private string currentStateText = "";
    [ObservableProperty] private string selectServiceOptionText = "";
    [ObservableProperty] private string serviceResetWarningText = "";
    [ObservableProperty] private string serviceValuesTitleText = "";
    [ObservableProperty] private string serviceValuesSubtitleText = "";
    [ObservableProperty] private string serviceValuesEmptyText = "";
    [ObservableProperty] private string directResetTitleText = "";
    [ObservableProperty] private string directResetSubtitleText = "";
    [ObservableProperty] private string guidedServiceTitleText = "";
    [ObservableProperty] private string guidedServiceSubtitleText = "";
    [ObservableProperty] private string guidedServiceEmptyText = "";
    [ObservableProperty] private string serviceCenterTitleText = "";
    [ObservableProperty] private string serviceCenterSubtitleText = "";
    [ObservableProperty] private string liveDataBadgeText = "";
    [ObservableProperty] private string directResetBadgeText = "";
    [ObservableProperty] private string advancedBadgeText = "";
    [ObservableProperty] private string resetButtonText = "";
    [ObservableProperty] private string guideButtonText = "";
    [ObservableProperty] private string noServiceContentTitleText = "";
    [ObservableProperty] private string noServiceContentText = "";
    [ObservableProperty] private string advancedToolsTitleText = "";
    [ObservableProperty] private string advancedToolsSubtitleText = "";
    [ObservableProperty] private bool isValidationCaptureRunning;
    [ObservableProperty] private string validationCaptureTitleText = "";
    [ObservableProperty] private string validationCaptureSubtitleText = "";
    [ObservableProperty] private string validationCaptureButtonText = "";
    [ObservableProperty] private string validationCaptureStatusText = "";
    public bool HasValidationCaptureStatus => !string.IsNullOrWhiteSpace(ValidationCaptureStatusText);
    partial void OnValidationCaptureStatusTextChanged(string value) =>
        OnPropertyChanged(nameof(HasValidationCaptureStatus));

    public ServiceViewModel(
    OBDService obd,
    VehicleInfoService vehicle,
    VehicleProfileResolverService vehicleProfiles,
    OemOperationService oemOperations,
    OemCodingBackupService codingBackups,
    OemDataCaptureService oemDataCapture,
    ServiceProcedureCatalogService serviceProcedureCatalog,
    ServiceDataReaderService serviceDataReader,
    ServiceResetVerificationService serviceResetVerifier,
    LocalizationService localization,
    VehicleBrandProfileService brandProfiles,
    MaintenanceCalendarService maintenanceCalendar)
    {
        _obd = obd;
        _vehicle = vehicle;
        _brandProfiles = brandProfiles;
        _vehicleProfiles = vehicleProfiles;
        _oemOperations = oemOperations;
        _oemDataCapture = oemDataCapture;
        _codingBackups = codingBackups;
        _serviceProcedureCatalog = serviceProcedureCatalog;
        _serviceDataReader = serviceDataReader;
        _serviceResetVerifier = serviceResetVerifier;
        _localization = localization;
        _maintenanceCalendar = maintenanceCalendar;
        _appOilReminder = new AppOilReminderService(obd);

        Attach();

        ApplyLanguageTexts();
        ClearOperationLists();
    }

    // Detached by ServicePage when it goes away (no leak through singleton services).
    private bool _attached;

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _oemOperations.ProgressChanged += OnOemProgressChanged;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _oemOperations.ProgressChanged -= OnOemProgressChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnOemProgressChanged(object? sender, string message) =>
        MainThread.BeginInvokeOnMainThread(() => ServiceResult = message);

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => _ = RefreshLanguageAsync());

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    partial void OnServiceResultChanged(string value) =>
        HasServiceMessage = !string.IsNullOrWhiteSpace(value);

    public void CancelRefresh() => _refreshCancellation?.Cancel();

    private async Task RefreshLanguageAsync()
    {
        ApplyLanguageTexts();

        if (ShowAppOilReminder && _appOilVehicleKey is not null)
            ApplyAppOilStatus(_appOilReminder.Peek(_appOilVehicleKey, _appOilMake));

        RebuildServiceValuePresentation();

        // Re-create guided instructions in the new language.
        if (!IsBusy)
        {
            foreach (var manual in MaintenanceProcedures.Where(item => item.IsManualProcedure).ToArray())
                MaintenanceProcedures.Remove(manual);

            try { await AddGuidedServiceResetsAsync(CancellationToken.None); }
            catch { }
        }
    }

    private void ApplyLanguageTexts()
    {
        PageTitleText = L("სერვისი", "Service");
        HeaderTitleText = L("სერვისი", "Service");

        RefreshButtonText = L("განახლება", "Refresh");

        ServiceReminderTitleText = L(
            "სერვისის შეხსენების განულება",
            "RESET SERVICE REMINDER");

        CompatibilityConfirmedText = L(
            "თავსებადობა დადასტურებულია:",
            "Compatibility confirmed with:");

        CurrentStateText = L(
            "მიმდინარე მდგომარეობა:",
            "Current state:");

        SelectServiceOptionText = L(
            "აირჩიე სერვისის მოქმედება:",
            "Select service option:");

        ServiceResetWarningText = L(
            "ყურადღება! Reset გამოიყენე მხოლოდ შესაბამისი სერვისის შესრულების შემდეგ.",
            "ATTENTION! Use reset only after the required service has been completed.");

        ServiceValuesTitleText = L("SERVICE STATUS", "SERVICE STATUS");
        ServiceValuesSubtitleText = L(
            "მხოლოდ ECU-დან რეალურად წაკითხული ზეთის/სერვისის მნიშვნელობები",
            "Only oil/service values actually read from the ECU");
        ServiceValuesEmptyText = L(
            "ამ მანქანამ ამ კავშირზე დადასტურებული Service მნიშვნელობა არ დააბრუნა.",
            "This vehicle did not return a verified service value on this connection.");

        DirectResetTitleText = L("ხელმისაწვდომი განულება", "Available reset");
        DirectResetSubtitleText = L(
            "ღილაკი ჩანს მხოლოდ exact vehicle/ECU-ზე reviewed reset operation-ის აღმოჩენისას. წარმატების შემდეგ მნიშვნელობა ECU-დან თავიდან იკითხება.",
            "A button appears only when a reviewed reset operation exists for the exact vehicle/ECU. After execution the value is read from the ECU again.");
        GuidedServiceTitleText = L(
            "GUIDED SERVICE",
            "GUIDED SERVICE");
        GuidedServiceSubtitleText = L(
            "ესენი არის მანქანისთვის დადასტურებული manual პროცედურები. ისინი ECU-ში write ბრძანებას არ აგზავნიან.",
            "These are vehicle-specific guided/manual procedures. They do not send an ECU write command.");
        GuidedServiceEmptyText = L(
            "ამ მანქანისთვის guided პროცედურა არ მოიძებნა.",
            "No guided service procedure was found for this vehicle.");

        ServiceCenterTitleText = L("SERVICE CENTER", "SERVICE CENTER");
        ServiceCenterSubtitleText = L(
            "რეალური ECU მონაცემები · დინამიური ფუნქციები · Reset-ის შემდგომი გადამოწმება",
            "Real ECU data · dynamic capabilities · post-reset verification");
        LiveDataBadgeText = L("LIVE DATA", "LIVE DATA");
        DirectResetBadgeText = L("DIRECT RESET", "DIRECT RESET");
        AdvancedBadgeText = L("ADVANCED", "ADVANCED");
        ResetButtonText = L("RESET", "RESET");
        GuideButtonText = L("ინსტრუქცია", "GUIDE");
        NoServiceContentTitleText = L(
            "სერვისის მონაცემი მიუწვდომელია",
            "Service data unavailable");
        NoServiceContentText = L(
            "ამ მანქანამ ამ კავშირზე მხარდაჭერილი სერვისის მნიშვნელობა არ დააბრუნა.",
            "This vehicle did not return a supported service value on this connection.");
        AdvancedToolsTitleText = L("დამატებითი მხარდაჭერილი ფუნქციები", "Additional supported functions");
        AdvancedToolsSubtitleText = L(
            "მხოლოდ ამ კონკრეტულ მანქანა/ECU-ზე აღმოჩენილი Adaptation, Coding და Active Test ფუნქციები.",
            "Only Adaptation, Coding and Active Test functions detected for this exact vehicle/ECU.");
        ValidationCaptureTitleText = L("SERVICE RESET-ის შემოწმება", "SERVICE RESET VALIDATION");
        ValidationCaptureSubtitleText = L(
            "ქმნის VIN-ის გარეშე read-only ტექნიკურ JSON ანგარიშს. არც reset და არც ECU write ბრძანება არ იგზავნება.",
            "Creates a read-only technical JSON report without the VIN. No reset or ECU write command is sent.");
        ValidationCaptureButtonText = L("READ-ONLY ანგარიშის შექმნა", "CREATE READ-ONLY REPORT");

        PageTitleText = L("სერვისი", "Service");
        HeaderTitleText = L("სერვისი", "Service");
        HeaderSubtitleText = L(
            "მხოლოდ დადასტურებული Service Reset, Adaptation, Coding და Active Test ფუნქციები",
            "Only verified service reset, adaptation, coding and active-test functions");
        RefreshButtonText = L("განახლება", "Refresh");
        VehicleCardTitleText = L("მანქანა / ECU კავშირი", "Vehicle / ECU connection");
        SafetyTitleText = L("უსაფრთხოების მდგომარეობა", "Safety status");
        SafetySubtitleText = L(
            "ეს არის მიმდინარე read-only snapshot. თითოეული ოპერაცია გაშვებამდე პირობებს თავიდან ამოწმებს.",
            "This is a read-only snapshot. Every operation re-checks its preconditions before execution.");
        VoltageLabelText = L("12V ძაბვა", "12V voltage");
        StationaryLabelText = L("მანქანის სიჩქარე", "Vehicle speed");
        EngineLabelText = L("ძრავის მდგომარეობა", "Engine state");
        ResultTitleText = L("ბოლო მოქმედება", "Last operation");

        MaintenanceTitleText = L("SERVICE RESETS", "SERVICE RESETS");
        MaintenanceSubtitleText = L(
            "ზეთის სერვისი, EPB და სხვა ზუსტად დადასტურებული სერვის-პროცედურები",
            "Oil service, EPB and other exactly verified service procedures");
        AdaptationTitleText = L("ADAPTATIONS", "ADAPTATIONS");
        AdaptationSubtitleText = L(
            "კალიბრაცია, relearn და adaptation პროცედურები მხოლოდ catalog-ში დადასტურებული ოპერაციებიდან",
            "Calibration, relearn and adaptation procedures only from verified catalog operations");
        CodingTitleText = L("CODING", "CODING");
        CodingSubtitleText = L(
            "Coding მხოლოდ exact OEM definition-ზე: backup → write → verify",
            "Coding only for an exact OEM definition: backup → write → verify");
        ActiveTestsTitleText = L("ACTIVE TESTS", "ACTIVE TESTS");
        ActiveTestsSubtitleText = L(
            "ტესტი ირთვება მხოლოდ reviewed ECU command-ით და უსაფრთხო STOP ბრძანებით",
            "Tests run only with a reviewed ECU command and a safe STOP command");

        RunServiceButtonText = L("გაშვება", "Run");
        RunAdaptationButtonText = L("ადაპტაციის დაწყება", "Start adaptation");
        RunCodingButtonText = L("კოდირების დაწყება", "Start coding");
        RunActiveTestButtonText = L("ტესტის გაშვება", "Run test");
        StopActiveTestButtonText = L("ACTIVE TEST-ის გაჩერება", "STOP ACTIVE TEST");
        RestoreBackupButtonText = L("Original Coding Backup-ის აღდგენა", "Restore original coding backup");

        MaintenanceEmptyText = L(
            "ამ მანქანისთვის დადასტურებული Service Reset ჯერ არ არის ხელმისაწვდომი.",
            "No verified service reset is currently available for this vehicle.");
        AdaptationEmptyText = L(
            "ამ მანქანისთვის დადასტურებული Adaptation ჯერ არ არის ხელმისაწვდომი.",
            "No verified adaptation is currently available for this vehicle.");
        CodingEmptyText = L(
            "ამ მანქანისთვის დადასტურებული Coding ფუნქცია ჯერ არ არის ხელმისაწვდომი.",
            "No verified coding function is currently available for this vehicle.");
        ActiveTestsEmptyText = L(
            "ამ მანქანისთვის დადასტურებული Active Test ჯერ არ არის ხელმისაწვდომი.",
            "No verified active test is currently available for this vehicle.");

        SafetyNoticeTitleText = L("უსაფრთხოების პრინციპი", "Safety principle");
        SafetyNoticeText = L(
            "აპი არ აგზავნის guessed OEM ბრძანებებს. ფუნქცია აქტიურდება მხოლოდ მაშინ, როცა ზუსტი მანქანა, ECU definition, მისამართი, ძაბვა და საჭირო runtime პირობები დადასტურებულია. Security Access-ის საჭიროებისას ფუნქცია დაბლოკილი რჩება, სანამ შესაბამისი ავტორიზაცია არ არსებობს.",
            "The app never sends guessed OEM commands. A function is enabled only when the exact vehicle, ECU definition, address, voltage and required runtime conditions are verified. Operations requiring Security Access remain blocked until a valid authorization provider exists.");

        DiscoveryTitleText = L("ზეთის და სერვისის ძებნა", "Oil & service search");
        DiscoveryHintText = L(
            "ჩაწერე მანქანის ეკრანზე ნაჩვენები ზეთის % და დააჭირე ძებნას. აპი სცდის ყველა ცნობილ გზას (ყველა მარკის) და პირველზე არ ჩერდება. Service due / ტექდათვალიერების მნიშვნელობაზე დააჭირე, თუ პანელზეც ასეა. მხოლოდ კითხულობს — მანქანაში არაფერს წერს.",
            "Enter the oil % shown on the car's display and start the search. The app tries every known source (all makes) and does not stop at the first one. Tap a Service due / inspection value if the dashboard shows the same. It only reads — nothing is written to the car.");
        DiscoveryQuickButtonText = L("სწრაფი შემოწმება", "Quick check");
        DiscoveryDeepButtonText = L("ძებნა", "Search");
        DiscoveryPlaceholderText = L("ეკრანის ზეთის % (მაგ. 90)", "Dashboard oil % (e.g. 90)");

        OilRefreshButtonText = L("განახლება", "Refresh");
        ResetFullButtonText = L("სრული განახლება (100%)", "Reset full length (100%)");
        ResetHalfButtonText = L("ნახევარი (50%)", "Reset 1/2 length (50%)");
        OilResetHistoryTitleText = L("განულების ისტორია:", "Reset history:");
        OilResetUnavailableText = L(
            "ამ მანქანაზე აპიდან განულება ჯერ მიუწვდომელია — გამოიყენე ქვემოთ მოცემული ინსტრუქცია.",
            "In-app reset is not available for this car yet — use the guide below.");
        if (HasOilResetCard)
            UpdateOilCompatibilityText();

        OnPropertyChanged(nameof(AppOilTitleText));
        OnPropertyChanged(nameof(AppOilButtonText));
        OnPropertyChanged(nameof(ReminderResetButtonText));

        Title = PageTitleText;
    }

    private void ClearOperationLists()
    {
        MaintenanceProcedures.Clear();
        AdaptationProcedures.Clear();
        CodingFeatures.Clear();
        ActiveTests.Clear();
        GuidedServiceProcedures.Clear();
        ServiceValues.Clear();
        ServiceReminders.Clear();
        HasServiceReminders = false;
        _lastServiceReadings = Array.Empty<ServiceMetricReading>();
        HasOilResetCard = false;
        CanResetOilLife = false;
        ServiceValuesStatusText = "";
        CanRestoreCodingBackup = false;
        CodingBackupState = L("Backup: —", "Backup: —");
        UpdateCollectionState();
    }

    private void UpdateCollectionState()
    {
        ShowMaintenanceEmpty = MaintenanceProcedures.Count == 0;
        ShowAdaptationEmpty = AdaptationProcedures.Count == 0;
        ShowCodingEmpty = CodingFeatures.Count == 0;
        ShowActiveTestsEmpty = ActiveTests.Count == 0;

        MaintenanceCountText = MaintenanceProcedures.Count.ToString();
        AdaptationCountText = AdaptationProcedures.Count.ToString();
        CodingCountText = CodingFeatures.Count.ToString();
        ActiveTestCountText = ActiveTests.Count.ToString();

        ShowServiceValuesEmpty = ServiceValues.Count == 0;
        ServiceValueCountText = ServiceValues.Count.ToString();

        ShowGuidedServiceEmpty = GuidedServiceProcedures.Count == 0;
        GuidedServiceCountText = GuidedServiceProcedures.Count.ToString();

        HasServiceValues = ServiceValues.Count > 0;
        HasDirectResets = MaintenanceProcedures.Count > 0;
        HasGuidedService = GuidedServiceProcedures.Count > 0;
        HasAdaptations = AdaptationProcedures.Count > 0;
        HasCodingFeatures = CodingFeatures.Count > 0;
        HasActiveTests = ActiveTests.Count > 0;
        HasAnyAdvancedOperations =
            HasAdaptations || HasCodingFeatures || HasActiveTests;

        var advancedCount =
            AdaptationProcedures.Count +
            CodingFeatures.Count +
            ActiveTests.Count;

        AdvancedOperationCountText =
            advancedCount.ToString();

        ShowNoServiceContent = !HasOilResetCard && !HasServiceReminders && !ShowAppOilReminder && !IsBusy;

        ServiceOverviewText = L(
            $"ECU მონაცემი: {ServiceValues.Count} · Direct Reset: {MaintenanceProcedures.Count} · დამატებითი: {advancedCount}",
            $"ECU values: {ServiceValues.Count} · Direct resets: {MaintenanceProcedures.Count} · Advanced: {advancedCount}");
    }

    [RelayCommand]
    public async Task Refresh()
    {
        try
        {
        if (IsBusy)
            return;

        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        var token = _refreshCancellation.Token;

        try
        {
            IsBusy = true;
            _currentOemVehicle = null;
            ShowNoServiceContent = false;

            ConnectionState = "LOADING";
            ConnectionStateColor = "#1D5BD6";
            ConnectionDetail = L(
                "ვამოწმებ OBD adapter-ს და ECU-ს…",
                "Checking the OBD adapter and ECU…");
            OemState = "LOADING";
            OemStateColor = "#1D5BD6";
            OemDetail = L("OEM catalog და ECU პასუხები მოწმდება…", "Checking the OEM catalog and ECU responses…");
            ServiceResult = L(
                "სერვისის მონაცემები იკითხება…",
                "Reading service data…");

            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();

            token.ThrowIfCancellationRequested();
            var adapter = await _obd.GetAdapterDiagnosticsAsync(token);
            ApplyAdapterDiagnostics(adapter);
            await RefreshSafetySnapshotAsync(adapter);
            token.ThrowIfCancellationRequested();

            if (!IsGenericObdReady)
            {
                ClearOperationLists();
                OemState = L("OEM ფუნქციები დაბლოკილია", "OEM functions blocked");
                OemStateColor = "#C98A1A";
                OemDetail = L(
                    "რეალური Generic OBD-II ECU პასუხი ვერ დადასტურდა, ამიტომ Service/Coding ბრძანება არ გაიგზავნება.",
                    "A real generic OBD-II ECU response was not confirmed, so no Service/Coding command will be sent.");
                ServiceResult = OemDetail;
                return;
            }

            VehicleInfo vehicle;
            try
            {
                // Reuse the identity already read at connection time (joins
                // the shared read if it is still running) instead of a slow
                // full re-read that could time out.
                using var identityTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                identityTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                vehicle = await _vehicle.EnsureConnectedVehicleInfoAsync(
                    forceVinRefresh: false,
                    identityTimeout.Token);
            }
            catch (Exception ex)
            {
                ClearOperationLists();
                VehicleSummary = L("ავტომობილი ვერ დადასტურდა", "Vehicle not verified");
                VehicleVin = "VIN: —";
                OemState = L("OEM ფუნქციები დაბლოკილია", "OEM functions blocked");
                OemStateColor = "#C98A1A";
                OemDetail = L(
                    "ECU პასუხობს, მაგრამ VIN/მწარმოებელი/მოდელი საკმარისად ვერ დადასტურდა.",
                    "The ECU responds, but VIN/make/model could not be verified sufficiently.");
                ServiceResult = $"{OemDetail} {UserError.Describe(ex)}".Trim();
                return;
            }

            ApplyVehicleIdentity(vehicle);

            _currentOemVehicle = vehicle;

            var resolvedVehicle = _vehicleProfiles.Resolve(vehicle);

            if (!resolvedVehicle.CanUseManufacturerSpecificDefinitions)
            {
                ClearOperationLists();

                // GM oil life is a read-only value that does not need the
                // full OEM profile; show it (and the search) anyway.
                try { await RefreshServiceValuesAsync(vehicle, token); }
                catch (OperationCanceledException) { throw; }
                catch { }
                ShowOilDiscovery = true;
                try { await TryKnownSourcesAsync(vehicle, token); }
                catch (OperationCanceledException) { throw; }
                catch { }

                OemState = L(
                    "OEM პროფილი დაუდასტურებელია",
                    "OEM profile not verified");

                OemStateColor = "#C98A1A";
                OemDetail = resolvedVehicle.VerificationMessage;
                ServiceResult = OemDetail;
                return;
            }

            // Read-only Service Engine V2: only values positively returned by
            // the exact connected vehicle are shown. No reset command is sent here.
            await RefreshServiceValuesAsync(vehicle, token);

            var availability = await _oemOperations.DetectAsync(
                vehicle,
                knownDiscovery: null,
                knownAdapter: adapter,
                token: token);

            // Apply verified OEM operations (executable ECU-based resets)
            ApplyOemOperations(availability);

            UpdateCollectionState();

            var executable = MaintenanceProcedures.Count(item => !item.IsManualProcedure);
            OemState = executable > 0
                ? L("OEM ფუნქციები ხელმისაწვდომია", "OEM functions available")
                : L("OEM ფუნქციები ჯერ არ არის მზად", "OEM functions not ready");
            OemStateColor = executable > 0 ? "#1F8A4C" : "#C98A1A";
            OemDetail = availability.Detail;

            ServiceResult = executable > 0
                ? L(
                    $"ECU-დან განახლდა {_lastServiceReadings.Count} სერვისის მნიშვნელობა. ხელმისაწვდომია {executable} დადასტურებული reset.",
                    $"Updated {_lastServiceReadings.Count} service value(s) from the ECU. {executable} verified reset(s) available.")
                : _lastServiceReadings.Count > 0
                    ? L(
                        $"ECU-დან განახლდა {_lastServiceReadings.Count} სერვისის მნიშვნელობა.",
                        $"Updated {_lastServiceReadings.Count} service value(s) from the ECU.")
                    : L(
                        "ამ მანქანამ მხარდაჭერილი სერვისის მნიშვნელობა არ დააბრუნა.",
                        "This vehicle did not return a supported service value.")
                      + (string.IsNullOrWhiteSpace(_lastServiceReadDetail)
                          ? string.Empty
                          : "\n\n" + _lastServiceReadDetail);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            ServiceResult = string.Empty;
        }
        catch (Exception ex)
        {
            SetErrorState(ex);
        }
        finally
        {
            // Guided (dashboard/button) service resets work on every car and
            // need no OBD verification, so they are always shown — also when
            // the adapter is not connected or the OEM profile is unverified.
            if (!token.IsCancellationRequested)
            {
                try
                {
                    await AddGuidedServiceResetsAsync(token);
                }
                catch
                {
                    // Instructions are optional; never break the page.
                }

                try
                {
                    await UpdateAppOilReminderAsync(token);
                }
                catch
                {
                    // Optional; never break the page.
                }

                try
                {
                    await RefreshMaintenancePartsAsync(token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"Maintenance parts refresh failed: {ex.Message}");
                }
            }

            IsBusy = false;
            UpdateCollectionState();
            _refreshCancellation?.Dispose();
            _refreshCancellation = null;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ServiceViewModel.Refresh");
        }
    }

    private async Task AddGuidedServiceResetsAsync(CancellationToken token)
    {
        var vehicle = _currentOemVehicle ?? _vehicle.CurrentVehicle;
        ShowOilDiscovery = true;
        try { await TryKnownSourcesAsync(vehicle, token); }
        catch (OperationCanceledException) { throw; }
        catch { }
        await LoadGuidedServiceProceduresAsync(vehicle, token);

        foreach (var guided in GuidedServiceProcedures)
        {
            if (MaintenanceProcedures.Any(m =>
                    !string.IsNullOrWhiteSpace(m.Id) &&
                    m.Id.Equals(guided.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            MaintenanceProcedures.Add(guided);
        }

        UpdateCollectionState();

        if (MaintenanceProcedures.Count > 0 && string.IsNullOrWhiteSpace(ServiceResult))
        {
            ServiceResult = L(
                "ქვემოთ არის შენი მანქანის სერვისის განულების ინსტრუქციები. დააჭირე „ინსტრუქცია“-ს.",
                "Below are the service reset instructions for your car. Tap GUIDE.");
        }
    }

    [RelayCommand]
    private async Task CaptureOemData()
    {
        try
        {
        if (IsBusy)
            return;

        if (_currentOemVehicle is null)
        {
            ValidationCaptureStatusText = L(
                "ჯერ მანქანის ინფორმაცია უნდა ჩაიტვირთოს.",
                "Vehicle information must be loaded first.");

            return;
        }

        try
        {
            IsBusy = true;
            IsValidationCaptureRunning = true;

            ValidationCaptureStatusText = L(
                "ECU-ის read-only პასუხები იკითხება…",
                "Reading ECU responses in read-only mode…");

            var path =
                await _oemDataCapture.CaptureAndSaveAsync(
                    _currentOemVehicle,
                    _lastServiceReadings);

            ValidationCaptureStatusText = L(
                "✓ უსაფრთხო ანგარიში შეიქმნა. აირჩიე სად გააზიარო ან შეინახო.",
                "✓ Safe report created. Choose where to share or save it.");

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = L("GCar360 Service Reset შემოწმება", "GCar360 Service Reset Validation"),
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            ValidationCaptureStatusText = L(
                $"ანგარიში ვერ შეიქმნა: {UserError.Describe(ex)}",
                $"Could not create the report: {UserError.Describe(ex)}");
        }
        finally
        {
            IsValidationCaptureRunning = false;
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ServiceViewModel.CaptureOemData");
        }
    }

    private void ApplyVehicleIdentity(VehicleInfo vehicle)
    {
        var parts = new[] { vehicle.Manufacturer, vehicle.Model, vehicle.Year }
            .Where(value => !string.IsNullOrWhiteSpace(value) &&
                            !string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase) &&
                            value != "—")
            .ToArray();

        VehicleSummary = parts.Length == 0
            ? L("ავტომობილი ამოცნობილია, მაგრამ სახელი მიუწვდომელია", "Vehicle identified, but name is unavailable")
            : string.Join(" ", parts);

        VehicleVin = string.IsNullOrWhiteSpace(vehicle.VIN) ||
                     string.Equals(vehicle.VIN, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? "VIN: —"
            : $"VIN: {vehicle.VIN}";
    }

    private void ApplyAdapterDiagnostics(AdapterDiagnostics adapter)
    {
        AdapterSummary = $"Adapter: {adapter.AdapterName} · Protocol: {adapter.Protocol} · Voltage: {adapter.Voltage}";
        IsGenericObdReady = adapter.BluetoothConnected && adapter.AdapterInitialized && adapter.VehicleResponding;

        if (!adapter.BluetoothConnected || !adapter.AdapterInitialized)
        {
            ConnectionState = "ERROR";
            ConnectionStateColor = "#B3361F";
            ConnectionDetail = L(
                "OBD adapter მზად არ არის. ჯერ დაუკავშირდი adapter-ს.",
                "The OBD adapter is not ready. Connect the adapter first.");
            return;
        }

        if (!adapter.VehicleResponding)
        {
            ConnectionState = "NO ECU RESPONSE";
            ConnectionStateColor = "#C98A1A";
            ConnectionDetail = L(
                "Adapter დაკავშირებულია, მაგრამ მანქანის Generic OBD-II ECU პასუხი ვერ დადასტურდა.",
                "The adapter is connected, but a generic OBD-II ECU response was not confirmed.");
            return;
        }

        ConnectionState = "READY";
        ConnectionStateColor = "#1F8A4C";
        ConnectionDetail = L(
            "Adapter და Generic OBD-II ECU პასუხი დადასტურებულია. OEM ფუნქციები ცალკე მოწმდება exact definition-ით.",
            "The adapter and generic OBD-II ECU response are confirmed. OEM functions are checked separately against exact definitions.");
    }

    private async Task RefreshSafetySnapshotAsync(AdapterDiagnostics adapter)
    {
        if (TryNumeric(adapter.Voltage, out var voltage))
        {
            VoltageState = $"{voltage:0.0} V";
            VoltageStateColor = voltage is >= 11.5 and <= 15.2 ? "#1F8A4C" : "#C98A1A";
        }
        else
        {
            VoltageState = L("ვერ დადასტურდა", "Not verified");
            VoltageStateColor = "#94A3B8";
        }

        if (!IsGenericObdReady)
        {
            SpeedState = L("ვერ დადასტურდა", "Not verified");
            SpeedStateColor = "#94A3B8";
            EngineState = L("ვერ დადასტურდა", "Not verified");
            EngineStateColor = "#94A3B8";
            return;
        }

        try
        {
            var speed = await _obd.ReadSensorAsync(0x0D);
            if (speed is not null && TryNumeric(speed.Value, out var speedValue))
            {
                SpeedState = speedValue < 0.5
                    ? L("გაჩერებულია", "Stationary")
                    : $"{speedValue:0} km/h";
                SpeedStateColor = speedValue < 0.5 ? "#1F8A4C" : "#C98A1A";
            }
            else
            {
                SpeedState = L("ვერ დადასტურდა", "Not verified");
                SpeedStateColor = "#94A3B8";
            }
        }
        catch
        {
            SpeedState = L("ვერ დადასტურდა", "Not verified");
            SpeedStateColor = "#94A3B8";
        }

        try
        {
            var rpm = await _obd.ReadSensorAsync(0x0C);
            if (rpm is not null && TryNumeric(rpm.Value, out var rpmValue))
            {
                EngineState = rpmValue < 50
                    ? L("OFF", "OFF")
                    : L($"RUNNING · {rpmValue:0} RPM", $"RUNNING · {rpmValue:0} RPM");
                EngineStateColor = "#1F8A4C";
            }
            else
            {
                EngineState = L("ვერ დადასტურდა", "Not verified");
                EngineStateColor = "#94A3B8";
            }
        }
        catch
        {
            EngineState = L("ვერ დადასტურდა", "Not verified");
            EngineStateColor = "#94A3B8";
        }
    }

    private async Task LoadGuidedServiceProceduresAsync(
    VehicleInfo? vehicle,
    CancellationToken token = default)
    {
        GuidedServiceProcedures.Clear();

        // When the manufacturer was not read from the VIN, fall back to the
        // brand the user selected before connecting.
        var selectedBrand = _brandProfiles.Current;
        var fallbackBrand = selectedBrand.Id.Equals("other", StringComparison.OrdinalIgnoreCase)
            ? null
            : selectedBrand.DisplayName;

        var procedures =
            await _serviceProcedureCatalog
                .GetForVehicleAsync(vehicle, fallbackBrand, token);

        foreach (var procedure in procedures)
        {
            // ECU-changing diagnostic operations are managed exclusively by
            // OemOperationService. This collection is intentionally manual.
            if (procedure.ExecutionType != ServiceProcedureExecutionType.Manual)
                continue;

            var name = _localization.IsEnglish
                ? procedure.NameEn
                : procedure.NameKa;

            var description = _localization.IsEnglish
                ? procedure.DescriptionEn
                : procedure.DescriptionKa;

            var instructions = string.Join(
                Environment.NewLine + Environment.NewLine,
                procedure.Steps
                    .OrderBy(step => step.Order)
                    .Select(step =>
                    {
                        var text = _localization.IsEnglish
                            ? step.TextEn
                            : step.TextKa;

                        return $"{step.Order}. {text}";
                    }));

            GuidedServiceProcedures.Add(new ServiceProcedureItem
            {
                Id = procedure.Id,
                Name = name,
                Group = procedure.Category,
                Description = description,
                OperationId = "",
                IsManualProcedure = true,
                ManualInstructions = instructions,
                ButtonText = GuideButtonText,
                IsActiveTest = false,
                IsAvailable = true,
                RequiresVerifiedOemProfile = false,
                AvailabilityOverride = L(
                    "GUIDED · ECU write არ იგზავნება",
                    "GUIDED · No ECU write is sent"),
                AvailabilityOverrideColor = "#1D5BD6"
            });
        }

        UpdateCollectionState();
    }

    private static void RemoveOemItems(
    ObservableCollection<ServiceProcedureItem> collection)
    {
        foreach (var item in collection
                     .Where(item => !item.IsManualProcedure)
                     .ToArray())
        {
            collection.Remove(item);
        }
    }


    private async Task RefreshServiceValuesAsync(
        VehicleInfo vehicle,
        CancellationToken token = default)
    {
        var result = await _serviceDataReader.ReadAsync(vehicle, token);
        _lastServiceReadings = result.Values;
        try
        {
            await _maintenanceCalendar.SyncServiceReadingsAsync(vehicle, result.Values);
        }
        catch
        {
            // Calendar synchronization is isolated from the existing service page.
        }
        ServiceValuesStatusText = result.Status;

        // Why no value was read: reader status + the ECU's raw reply to the
        // oil-life request (if one was sent). Shown on the page.
        var probe = _serviceDataReader.LastOilLifeProbeNote;
        _lastServiceReadDetail = result.HasValues
            ? string.Empty
            : string.Join("\n", new[]
                {
                    $"{result.Status}: {result.Detail}".Trim(' ', ':'),
                    string.IsNullOrWhiteSpace(probe) ? string.Empty : L($"ECU-ს პასუხი: {probe}", $"ECU reply: {probe}")
                }.Where(line => !string.IsNullOrWhiteSpace(line)));
        RebuildServiceValuePresentation();
    }

    private void RebuildServiceValuePresentation()
    {
        ServiceValues.Clear();

        // Oil life is shown in the "Reset service reminder" card.
        var oilLife = _lastServiceReadings.FirstOrDefault(item => item.Kind == ServiceMetricKind.OilLifePercent);
        HasOilResetCard = oilLife is not null;
        _oilLifeReading = oilLife;
        CanResetOilLife = oilLife is not null &&
            (IsLearnedGmOilReading(oilLife) ? _serviceDataReader.CanResetLearnedOilLife : _serviceDataReader.CanResetByWriteBack(oilLife));
        OilManualResetText = oilLife is not null && !CanResetOilLife ? ManualResetGuide() : string.Empty;
        if (oilLife is not null)
        {
            SetOilLifeDisplay(oilLife.NumericValue);
            UpdateOilCompatibilityText();
            UpdateOilResetHistoryText();
        }

        foreach (var reading in _lastServiceReadings)
        {
            if (oilLife is not null && ReferenceEquals(reading, oilLife))
                continue;

            ServiceValues.Add(new ServiceMetricDisplayItem
            {
                Id = reading.Id,
                Kind = reading.Kind,
                IconText = GetServiceMetricIcon(reading.Kind),
                Name = LocalizeServiceMetricName(reading),
                ValueText = FormatServiceMetricValue(reading),
                ValueColor = GetServiceMetricValueColor(reading),
                Detail = reading.Detail,
                SourceText = reading.SourceCatalog,
                EvidenceBadge = reading.EvidenceLevel == ServiceMetricEvidenceLevel.ProductionCatalog
                    ? L("REAL ECU READ", "REAL ECU READ")
                    : L("RESEARCH READ", "RESEARCH READ"),
                EvidenceColor = reading.EvidenceLevel == ServiceMetricEvidenceLevel.ProductionCatalog
                    ? "#1F8A4C"
                    : "#C98A1A"
            });
        }

        RebuildServiceReminders(oilLife);
        UpdateCollectionState();

        // The oil row of the parts list uses the same value from the car.
        StoreCarServiceValues(_lastServiceReadings);
    }

    // ------------------------------------------------------------------
    // Service reminders: one row per reminder the car really returned.
    // A reminder the car does not have is simply not shown.
    // ------------------------------------------------------------------

    private ServiceMetricReading? _oilLifeReading;

    // ---- App oil reminder (every car; for cars that do not report oil service) ----
    private readonly AppOilReminderService _appOilReminder;
    private string? _appOilVehicleKey;
    private string? _appOilMake;
    [ObservableProperty] private bool showAppOilReminder;
    [ObservableProperty] private bool isAppOilStarted;
    [ObservableProperty] private string appOilValueText = "—";
    [ObservableProperty] private double appOilFraction;
    [ObservableProperty] private string appOilColor = "#1F8A4C";
    [ObservableProperty] private string appOilHintText = "";
    [ObservableProperty] private string appOilManualText = "";
    [ObservableProperty] private bool isAppOilBusy;
    public string AppOilTitleText => L("ზეთის სერვისი — დარჩა:", "Oil service — remaining:");
    public string AppOilButtonText => L("ზეთი შევცვალე", "Oil changed");
    partial void OnShowAppOilReminderChanged(bool value) => UpdateCollectionState();

    private static string? VehicleReminderKey(VehicleInfo? vehicle) =>
        vehicle is not null && !string.IsNullOrWhiteSpace(vehicle.VIN) && vehicle.VIN.Trim().Length >= 11 &&
        !vehicle.VIN.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? vehicle.VIN.Trim().ToUpperInvariant()
            : null;

    private bool CarReportsOilService() =>
        HasOilResetCard || ServiceReminders.Any(row => row.Key == "oil");

    private async Task UpdateAppOilReminderAsync(CancellationToken token)
    {
        var vehicle = _currentOemVehicle ?? _vehicle.CurrentVehicle;
        var key = VehicleReminderKey(vehicle);
        if (key is null || CarReportsOilService())
        {
            ShowAppOilReminder = false;
            return;
        }

        _appOilVehicleKey = key;
        _appOilMake = vehicle?.Manufacturer;

        AppOilReminderStatus status;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            status = await _appOilReminder.UpdateAsync(key, _appOilMake, timeout.Token);
        }
        catch
        {
            status = _appOilReminder.Peek(key, _appOilMake);
        }

        // Every car that does not report its own oil service gets this card:
        // with an odometer it counts km + days, without one it counts days.
        ApplyAppOilStatus(status);
        AppOilManualText = ManualResetGuide();
        ShowAppOilReminder = true;
    }

    private void ApplyAppOilStatus(AppOilReminderStatus status)
    {
        IsAppOilStarted = status.IsStarted;
        if (!status.IsStarted)
        {
            AppOilValueText = "—";
            AppOilFraction = 0;
            AppOilColor = "#94A3B8";
            AppOilHintText = L(
                $"ზეთის შეცვლისას დააჭირე „ზეთი შევცვალე“ — აპი თავად დაითვლის დარჩენილ კილომეტრს და დღეებს ({status.IntervalKm:N0} კმ / 12 თვე).",
                $"Tap “Oil changed” after an oil change — the app counts the remaining distance and days ({status.IntervalKm:N0} km / 12 months).").Replace(',', ' ');
            return;
        }

        var overdue = status.DaysLeft < 0 || status.KmLeft < 0;
        var days = L($"{Math.Max(0, status.DaysLeft)} დღე", $"{Math.Max(0, status.DaysLeft)} days");
        AppOilValueText = overdue
            ? L("ვადა გასულია", "Overdue")
            : status.KmLeft.HasValue
                ? L($"{status.KmLeft.Value:N0} კმ · {days}", $"{status.KmLeft.Value:N0} km · {days}").Replace(',', ' ')
                : days;
        AppOilFraction = status.Fraction;
        AppOilColor = overdue || status.Fraction <= 0.1 ? "#B3361F" : status.Fraction <= 0.25 ? "#C98A1A" : "#1F8A4C";
        AppOilHintText = (status.KmLeft.HasValue
            ? L(
                $"ზეთი შეიცვალა: {status.ChangedLocal:dd.MM.yyyy} · ინტერვალი {status.IntervalKm:N0} კმ / 12 თვე",
                $"Oil changed: {status.ChangedLocal:dd.MM.yyyy} · interval {status.IntervalKm:N0} km / 12 months")
            : L(
                $"ზეთი შეიცვალა: {status.ChangedLocal:dd.MM.yyyy} · ეს მანქანა OBD-ით გარბენს არ აბრუნებს, ამიტომ შეხსენება თარიღით ითვლის (12 თვე).",
                $"Oil changed: {status.ChangedLocal:dd.MM.yyyy} · this car does not report its odometer over OBD, so the reminder counts by date (12 months).")).Replace(',', ' ');
    }

    [RelayCommand]
    private async Task MarkOilChanged()
    {
        try
        {
        if (_appOilVehicleKey is null || IsAppOilBusy)
            return;

        try
        {
            var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null)
                return;

            var confirmed = await page.DisplayAlertAsync(
                AppOilButtonText,
                L(
                    "ზეთის სერვისის ათვლა თავიდან დაიწყება დღევანდელი დღიდან. დააჭირე მხოლოდ ზეთის შეცვლის შემდეგ.",
                    "The oil service countdown restarts from today. Tap only after an oil change."),
                L("დიახ", "Yes"),
                L("გაუქმება", "Cancel"));
            if (!confirmed)
                return;

            IsAppOilBusy = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var status = await _appOilReminder.MarkOilChangedAsync(_appOilVehicleKey, _appOilMake, timeout.Token);
            if (!status.IsStarted)
            {
                // Odometer not answering right now (adapter not connected).
                AppOilHintText = L(
                    "მანქანის ოდომეტრი ახლა არ იკითხება. შეაერთე ადაპტერი, ჩართე ანთება და სცადე ხელახლა.",
                    "The car's odometer can't be read right now. Connect the adapter, switch the ignition on and try again.");
                return;
            }

            ApplyAppOilStatus(status);
        }
        catch
        {
            // The countdown is stored even if the car did not answer.
            ApplyAppOilStatus(_appOilReminder.Peek(_appOilVehicleKey, _appOilMake));
        }
        finally
        {
            IsAppOilBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ServiceViewModel.MarkOilChanged");
        }
    }

    private static bool IsLearnedGmOilReading(ServiceMetricReading reading) =>
        reading.Id.StartsWith("gm_oil_life_learned", StringComparison.OrdinalIgnoreCase) ||
        reading.Id.StartsWith("chevrolet_volt_oil_life", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty] private string reminderStatusText = "";
    [ObservableProperty] private string reminderStatusColor = "#94A3B8";
    [ObservableProperty] private bool isReminderResetRunning;
    public bool HasReminderStatus => !string.IsNullOrWhiteSpace(ReminderStatusText);
    partial void OnReminderStatusTextChanged(string value) => OnPropertyChanged(nameof(HasReminderStatus));
    public string ReminderResetButtonText => L("განულება", "Reset");

    [RelayCommand]
    private async Task ResetReminder(ServiceReminderRow? row)
    {
        try
        {
        if (row?.ResetReading is not { } reading || IsBusy || IsOilResetRunning || IsReminderResetRunning)
            return;

        bool confirmed;
        try
        {
            var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null)
                return;

            confirmed = await page.DisplayAlertAsync(
                row.Title,
                L(
                    $"{row.Title} ({row.ValueText}) განულდება.\n\n• გააკეთე მხოლოდ სერვისის შესრულების შემდეგ.\n• მანქანა P-ზე, ანთება ჩართული.\n\nგაგრძელდეს?",
                    $"{row.Title} ({row.ValueText}) will be reset.\n\n• Do this only after the service was done.\n• Car in P, ignition on.\n\nContinue?"),
                L("განულება", "Reset"),
                L("გაუქმება", "Cancel"));
        }
        catch
        {
            return;
        }

        if (!confirmed)
            return;

        try
        {
            IsReminderResetRunning = true;
            ReminderStatusColor = "#1D5BD6";
            ReminderStatusText = L("განულება იგზავნება…", "Sending reset…");

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            if (!_obd.IsInitialized)
                await _obd.InitializeAsync(cancellation.Token);

            var result = await _serviceDataReader.ResetByWriteBackAsync(reading, 0, null, cancellation.Token);

            ReminderStatusColor = result.Success ? "#1F8A4C" : result.WriteSent ? "#B3361F" : "#C98A1A";
            ReminderStatusText = result.Success
                ? L($"✓ {row.Title} განულდა.", $"✓ {row.Title} was reset.")
                : DescribeOilResetFailure(result);

            if (_currentOemVehicle is not null)
                await RefreshServiceValuesAsync(_currentOemVehicle, cancellation.Token);
        }
        catch (Exception ex)
        {
            ReminderStatusColor = "#B3361F";
            ReminderStatusText = L($"განულება ვერ შესრულდა: {UserError.Describe(ex)}", $"Reset failed: {UserError.Describe(ex)}");
        }
        finally
        {
            IsReminderResetRunning = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ServiceViewModel.ResetReminder");
        }
    }

    private void RebuildServiceReminders(ServiceMetricReading? oilLifeCard)
    {
        ServiceReminders.Clear();

        var groups = _lastServiceReadings
            .Where(reading => !ReferenceEquals(reading, oilLifeCard))
            .Where(IsReminderReading)
            .GroupBy(ReminderGroupKey)
            .OrderBy(group => ReminderOrder(group.Key));

        foreach (var group in groups)
        {
            var readings = group.ToArray();
            var parts = new List<string>();

            foreach (var reading in readings.Where(r => r.Kind is ServiceMetricKind.DaysToNextService or ServiceMetricKind.InspectionDays))
                parts.Add(L($"{reading.NumericValue:0} დღე", $"{reading.NumericValue:0} days"));
            foreach (var reading in readings.Where(r => r.Kind is ServiceMetricKind.DistanceToNextService or ServiceMetricKind.InspectionDistance))
                parts.Add(L($"{reading.NumericValue:N0} კმ", $"{reading.NumericValue:N0} km").Replace(',', ' '));
            foreach (var reading in readings.Where(r => r.Kind is ServiceMetricKind.OilLifePercent))
                parts.Add($"{reading.NumericValue:0}%");
            foreach (var reading in readings.Where(r => r.Kind is ServiceMetricKind.DistanceSinceOilChange))
                parts.Add(L($"გავლილი {reading.NumericValue:N0} კმ", $"{reading.NumericValue:N0} km driven").Replace(',', ' '));
            foreach (var reading in readings.Where(r => r.Kind is ServiceMetricKind.MaintenanceCounter or ServiceMetricKind.ServiceStatus or ServiceMetricKind.Other))
                parts.Add(FormatServiceMetricValue(reading));

            if (parts.Count == 0)
                continue;

            var colors = readings.Select(GetServiceMetricValueColor).ToArray();
            var resettable = readings.FirstOrDefault(r =>
                r.Kind == ServiceMetricKind.DistanceSinceOilChange && _serviceDataReader.CanResetByWriteBack(r));
            ServiceReminders.Add(new ServiceReminderRow
            {
                Key = group.Key,
                ResetReading = resettable,
                ManualHint = resettable is null && group.Key is "oil" or "service" or "service_a" or "service_b"
                    ? ManualResetGuide()
                    : string.Empty,
                ResetButtonText = ReminderResetButtonText,
                Title = ReminderTitle(group.Key, readings[0]),
                ValueText = string.Join(" · ", parts),
                ValueColor = colors.Contains("#B3361F") ? "#B3361F" : colors.Contains("#C98A1A") ? "#C98A1A" : "#1F8A4C"
            });
        }

        HasServiceReminders = ServiceReminders.Count > 0;
    }

    // Only real reminders with a clear unit are shown (no status flags,
    // regeneration data or raw counters without a unit).
    private static bool IsReminderReading(ServiceMetricReading reading)
    {
        var unit = (reading.Unit ?? string.Empty).Trim().ToLowerInvariant();
        var hasUnit = unit is "percent" or "%" or "kilometers" or "kilometer" or "km" or "days" or "day";
        return reading.Kind switch
        {
            ServiceMetricKind.DistanceToNextService or
            ServiceMetricKind.DaysToNextService or
            ServiceMetricKind.DistanceSinceOilChange or
            ServiceMetricKind.InspectionDistance or
            ServiceMetricKind.InspectionDays or
            ServiceMetricKind.OilLifePercent => true,
            ServiceMetricKind.MaintenanceCounter => hasUnit,
            _ => false
        };
    }

    private static string ReminderGroupKey(ServiceMetricReading reading)
    {
        var name = $"{reading.SourceName} {reading.Id}".ToLowerInvariant();
        if (name.Contains("brake"))
            return "brake";
        if (reading.Kind is ServiceMetricKind.InspectionDays or ServiceMetricKind.InspectionDistance || name.Contains("inspection"))
            return "inspection";
        if (name.Contains("service a"))
            return "service_a";
        if (name.Contains("service b"))
            return "service_b";
        if (name.Contains("filter"))
            return "filter";
        if (name.Contains("oil") || reading.Kind is ServiceMetricKind.OilLifePercent or ServiceMetricKind.DistanceSinceOilChange)
            return "oil";
        if (reading.Kind is ServiceMetricKind.DistanceToNextService or ServiceMetricKind.DaysToNextService)
            return "service";
        return "other_" + reading.Id;
    }

    [ObservableProperty] private string oilManualResetText = "";
    public bool HasOilManualReset => !string.IsNullOrWhiteSpace(OilManualResetText);
    partial void OnOilManualResetTextChanged(string value) => OnPropertyChanged(nameof(HasOilManualReset));

    /// <summary>
    /// Brand-specific manual reset steps, shown when this car does not accept
    /// a verified OBD reset. After the manual reset, Refresh reads the new value.
    /// </summary>
    private string ManualResetGuide()
    {
        var vehicle = _currentOemVehicle ?? _vehicle.CurrentVehicle;
        var make = new string($"{vehicle?.Manufacturer} {_brandProfiles.Current.DisplayName}".Where(c => char.IsLetter(c) || c == ' ').ToArray()).ToLowerInvariant();
        bool Is(params string[] keys) => keys.Any(make.Contains);

        var head = L(
            "ამ მანქანაზე OBD-ით განულება დადასტურებული არ არის, ამიტომ ბრძანება არ იგზავნება. განულე ხელით, მერე დააჭირე „განახლებას“:\n",
            "This car has no verified OBD reset, so no command is sent. Reset it by hand, then tap Refresh:\n");

        string steps;
        if (Is("toyota", "lexus"))
            steps = L(
                "• ეკრანიანი დაფა: ანთება ON (ძრავა გამორთული) → საჭის ღილაკებით Settings (კბილანა) → Vehicle Settings → Scheduled Maintenance → Yes.\n• ძველი დაფა: ანთება OFF, ეკრანზე Trip A, დააჭირე და გეჭიროს Trip ღილაკი, ჩართე ანთება (ძრავის გარეშე) და გეჭიროს სანამ 000000 გამოჩნდება.",
                "• Display cluster: ignition ON (engine off) → steering buttons Settings (gear) → Vehicle Settings → Scheduled Maintenance → Yes.\n• Older cluster: ignition OFF, show Trip A, hold the Trip button, turn ignition ON (no engine) and keep holding until 000000 appears.");
        else if (Is("honda", "acura"))
            steps = L(
                "• ანთება ON → SEL/RESET-ით აჩვენე ზეთის %, გეჭიროს 10 წმ სანამ ციმციმს დაიწყებს, მერე კიდევ 5 წმ.\n• ახალ მოდელებზე: Settings → Vehicle → Maintenance Info → Reset.",
                "• Ignition ON → show oil % with SEL/RESET, hold 10 s until it blinks, then hold 5 s more.\n• Newer models: Settings → Vehicle → Maintenance Info → Reset.");
        else if (Is("chevrolet", "chevy", "gmc", "buick", "cadillac", "opel", "vauxhall", "holden"))
            steps = L(
                "• ანთება ON, ძრავა გამორთული: ნელა ბოლომდე დააჭირე გაზის პედალს 3-ჯერ 5 წამში.\n• ან დაფის მენიუ: Oil Life → გეჭიროს Select/Reset.",
                "• Ignition ON, engine off: slowly press the accelerator fully 3 times within 5 s.\n• Or cluster menu: Oil Life → hold Select/Reset.");
        else if (Is("ford", "lincoln", "mercury"))
            steps = L(
                "• ანთება ON → დაფის მენიუ Settings → Vehicle → Oil Life → გეჭიროს OK.\n• ძველ მოდელებზე: ანთება ON, ერთდროულად ბოლომდე დააჭირე გაზს და მუხრუჭს ~20 წმ.",
                "• Ignition ON → cluster Settings → Vehicle → Oil Life → hold OK.\n• Older models: ignition ON, press accelerator and brake fully together for ~20 s.");
        else if (Is("hyundai", "kia", "genesis"))
            steps = L("• ანთება ON → დაფა User Settings → Service Interval → Reset (ან მულტიმედია: Vehicle → Service).", "• Ignition ON → cluster User Settings → Service Interval → Reset (or infotainment: Vehicle → Service).");
        else if (Is("nissan", "infiniti"))
            steps = L("• ანთება ON → დაფა Settings → Maintenance → Oil → Reset.", "• Ignition ON → cluster Settings → Maintenance → Oil → Reset.");
        else if (Is("mazda"))
            steps = L("• ანთება ON → Settings → Maintenance → Scheduled → Reset.", "• Ignition ON → Settings → Maintenance → Scheduled → Reset.");
        else if (Is("volkswagen", "vw", "audi", "skoda", "seat", "cupra"))
            steps = L("• ანთება ON → დაფის მენიუ Settings → Service → Reset (ან MMI: Car → Service → Reset).", "• Ignition ON → cluster Settings → Service → Reset (or MMI: Car → Service → Reset).");
        else if (Is("bmw", "mini"))
            steps = L("• ანთება ON → გეჭიროს ოდომეტრის/Trip ღილაკი, სანამ სერვისის მენიუ გამოჩნდება → აირჩიე Oil → Reset.", "• Ignition ON → hold the odometer/Trip button until the service menu shows → choose Oil → Reset.");
        else if (Is("mercedes"))
            steps = L("• ანთება ON → საჭის მენიუ Service → ASSYST PLUS → Confirm service.", "• Ignition ON → steering menu Service → ASSYST PLUS → Confirm service.");
        else
            steps = L("• ანთება ON → დაფის ან მულტიმედიის Settings → Maintenance/Service → Reset. ზუსტი ნაბიჯები მანქანის ინსტრუქციაშია.", "• Ignition ON → cluster or infotainment Settings → Maintenance/Service → Reset. Exact steps are in the owner's manual.");

        return head + steps;
    }

    private static int ReminderOrder(string key) => key switch
    {
        "oil" => 0,
        "service" => 1,
        "service_a" => 2,
        "service_b" => 3,
        "inspection" => 4,
        "brake" => 5,
        "filter" => 6,
        _ => 9
    };

    private string ReminderTitle(string key, ServiceMetricReading first) => key switch
    {
        "oil" => L("ზეთის სერვისი", "Oil service"),
        "service" => L("სერვისი", "Service"),
        "service_a" => "Service A",
        "service_b" => "Service B",
        "inspection" => L("ტექდათვალიერება", "Inspection"),
        "brake" => L("სამუხრუჭე სითხე", "Brake fluid"),
        "filter" => L("ფილტრი", "Filter"),
        _ => LocalizeServiceMetricName(first)
    };

    private static string GetServiceMetricIcon(ServiceMetricKind kind) => kind switch
    {
        ServiceMetricKind.OilLifePercent => "OIL",
        ServiceMetricKind.OilLevel => "LVL",
        ServiceMetricKind.DistanceToNextService => "KM",
        ServiceMetricKind.DaysToNextService => "DAY",
        ServiceMetricKind.DistanceSinceOilChange => "KM",
        ServiceMetricKind.OilChangeInterval => "INT",
        ServiceMetricKind.InspectionDistance => "INSP",
        ServiceMetricKind.InspectionDays => "DAY",
        ServiceMetricKind.MaintenanceCounter => "CNT",
        ServiceMetricKind.ServiceStatus => "SRV",
        _ => "DATA"
    };

    private static string GetServiceMetricValueColor(ServiceMetricReading reading)
    {
        return reading.Kind switch
        {
            ServiceMetricKind.OilLifePercent when reading.NumericValue <= 10 => "#B3361F",
            ServiceMetricKind.OilLifePercent when reading.NumericValue <= 25 => "#C98A1A",
            ServiceMetricKind.OilLifePercent => "#1F8A4C",

            ServiceMetricKind.DistanceToNextService when reading.NumericValue <= 0 => "#B3361F",
            ServiceMetricKind.DistanceToNextService when reading.NumericValue <= 1000 => "#C98A1A",
            ServiceMetricKind.DistanceToNextService => "#1F8A4C",

            ServiceMetricKind.DaysToNextService when reading.NumericValue <= 0 => "#B3361F",
            ServiceMetricKind.DaysToNextService when reading.NumericValue <= 30 => "#C98A1A",
            ServiceMetricKind.DaysToNextService => "#1F8A4C",

            ServiceMetricKind.InspectionDistance when reading.NumericValue <= 0 => "#B3361F",
            ServiceMetricKind.InspectionDistance when reading.NumericValue <= 1000 => "#C98A1A",
            ServiceMetricKind.InspectionDistance => "#1F8A4C",

            ServiceMetricKind.InspectionDays when reading.NumericValue <= 0 => "#B3361F",
            ServiceMetricKind.InspectionDays when reading.NumericValue <= 30 => "#C98A1A",
            ServiceMetricKind.InspectionDays => "#1F8A4C",

            _ => "#1F8A4C"
        };
    }

    private string LocalizeServiceMetricName(ServiceMetricReading reading) => reading.Kind switch
    {
        ServiceMetricKind.OilLifePercent => L("ზეთის დარჩენილი რესურსი", "Oil Life Remaining"),

        // IMPORTANT: specific OilLevel patterns must come before the generic
        // OilLevel arm, otherwise the guarded patterns are unreachable.
        ServiceMetricKind.OilLevel when
            reading.SourceName.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
            reading.SourceName.Contains("warning", StringComparison.OrdinalIgnoreCase)
                => L("ზეთის დონის ზღვარი", "Oil level threshold"),

        ServiceMetricKind.OilLevel when
            reading.SourceName.Contains("max", StringComparison.OrdinalIgnoreCase)
                => L("ზეთის დონის მაქსიმუმი", "Oil level maximum"),

        ServiceMetricKind.OilLevel when
            reading.SourceName.Contains("min", StringComparison.OrdinalIgnoreCase)
                => L("ზეთის დონის მინიმუმი", "Oil level minimum"),

        ServiceMetricKind.OilLevel => L("ზეთის დონე", "Oil level"),

        ServiceMetricKind.DistanceToNextService => L("მანძილი შემდეგ სერვისამდე", "Distance to next service"),
        ServiceMetricKind.DaysToNextService => L("დღეები შემდეგ სერვისამდე", "Days to next service"),
        ServiceMetricKind.DistanceSinceOilChange => L("მანძილი ზეთის ბოლო შეცვლის შემდეგ", "Distance since last oil change"),
        ServiceMetricKind.OilChangeInterval => L("ზეთის შეცვლის ინტერვალი", "Oil change interval"),
        ServiceMetricKind.InspectionDistance => L("მანძილი ინსპექციამდე", "Distance to inspection"),
        ServiceMetricKind.InspectionDays => L("დღეები ინსპექციამდე", "Days to inspection"),
        ServiceMetricKind.MaintenanceCounter => reading.SourceName,
        ServiceMetricKind.ServiceStatus => L("სერვისის სტატუსი", "Service status"),
        _ => reading.SourceName
    };

    private string FormatServiceMetricValue(ServiceMetricReading reading)
    {
        var unit = (reading.Unit ?? string.Empty).Trim().ToLowerInvariant();
        return unit switch
        {
            "percent" or "%" => $"{reading.NumericValue:0.#}%",
            "kilometers" or "kilometer" or "km" => $"{reading.NumericValue:0} km",
            "days" or "day" => L($"{reading.NumericValue:0} დღე", $"{reading.NumericValue:0} days"),
            "centimeters" or "centimeter" or "cm" => $"{reading.NumericValue:0.##} cm",
            "offon" => reading.NumericValue >= 0.5 ? L("ჩართული", "ON") : L("გამორთული", "OFF"),
            "scalar" or "unknown" or "" => $"{reading.NumericValue:0.##}",
            _ => $"{reading.NumericValue:0.##} {reading.Unit}"
        };
    }

    private void ApplyOemOperations(OemOperationAvailabilityResult result)
    {
        MaintenanceProcedures.Clear();
        AdaptationProcedures.Clear();
        CodingFeatures.Clear();
        ActiveTests.Clear();

        foreach (var availability in result.Operations.Where(item =>
                     item.CanExecute &&
                     item.Definition.Type == OemOperationType.Service))
        {
            var item = new ServiceProcedureItem
            {
                Id = availability.Definition.OperationId,
                OperationId = availability.Definition.OperationId,
                Name = availability.Definition.Name,
                Group = availability.Definition.Group,
                Description = availability.Definition.Description,
                IsAvailable = availability.CanExecute,
                IsActiveTest = availability.Definition.Type == OemOperationType.ActiveTest,
                AvailabilityOverride = $"{availability.StatusText} · {availability.Detail}",
                AvailabilityOverrideColor = availability.CanExecute ? "#1F8A4C" : "#C98A1A"
            };

            MaintenanceProcedures.Add(item);
        }

        UpdateCollectionState();
    }

    private static bool IsAdaptationGroup(string? group)
    {
        if (string.IsNullOrWhiteSpace(group))
            return false;

        var value = group.Trim();
        return value.Contains("adapt", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("calibr", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("relearn", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("learning", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("basic setting", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshBackupStateAsync(
        VehicleInfo vehicle,
        OemOperationAvailabilityResult availability)
    {
        CanRestoreCodingBackup = false;

        if (string.IsNullOrWhiteSpace(vehicle.VIN) ||
            string.Equals(vehicle.VIN, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            CodingBackupState = L("Backup: VIN მიუწვდომელია", "Backup: VIN unavailable");
            return;
        }

        var backup = await _codingBackups.GetLatestAsync(vehicle.VIN);
        if (backup is null)
        {
            CodingBackupState = L("Backup: ჯერ არ არის შენახული", "Backup: none saved yet");
            return;
        }

        CanRestoreCodingBackup = availability.Operations.Any(item =>
            item.CanExecute &&
            item.Definition.Type == OemOperationType.Coding &&
            item.Definition.OperationId.Equals(backup.OperationId, StringComparison.OrdinalIgnoreCase));

        CodingBackupState = CanRestoreCodingBackup
            ? L("Backup: ხელმისაწვდომია ამ VIN-ზე", "Backup: available for this VIN")
            : L("Backup: ნაპოვნია, მაგრამ მიმდინარე ECU/პირობებით აღდგენა დაბლოკილია", "Backup: found, but restore is blocked by the current ECU/preconditions");
    }

    [RelayCommand]
    private async Task ExecuteOemOperation(ServiceProcedureItem? item)
    {
        try
        {
        if (item is null || !item.IsAvailable || IsBusy)
            return;

        if (item.IsManualProcedure)
        {
            // Guided procedure: only shows instructions, sends nothing to the
            // ECU. Guarded so a display error can never close the app.
            try
            {
                var message =
                    $"{item.Description}\n\n{item.ManualInstructions}".Trim();

                var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page is not null)
                {
                    await page.DisplayAlertAsync(
                        item.Name ?? string.Empty,
                        message,
                        L("დახურვა", "Close"));
                }

                ServiceResult = L(
                    $"{item.Name} — ინსტრუქცია ნაჩვენებია.",
                    $"{item.Name} — instructions displayed.");
            }
            catch (Exception ex)
            {
                ServiceResult = L(
                    $"ინსტრუქციის ჩვენება ვერ მოხერხდა: {UserError.Describe(ex)}",
                    $"Could not show the instructions: {UserError.Describe(ex)}");
            }

            return;
        }

        if (_currentOemVehicle is null)
        {
            ServiceResult = L(
                "ჯერ დააჭირე „შემოწმება“-ს და დაადასტურე მანქანა/ECU.",
                "Run Check first and verify the vehicle/ECU.");
            return;
        }

        var isOilLifeReset =
            item.Name.Contains("oil", StringComparison.OrdinalIgnoreCase) &&
            (item.Name.Contains("life", StringComparison.OrdinalIgnoreCase) ||
             item.Description.Contains("oil", StringComparison.OrdinalIgnoreCase));

        var confirmed = await Shell.Current.DisplayAlertAsync(
            isOilLifeReset
                ? L("ზეთის რესურსის განულება?", "Reset Oil Life?")
                : item.Name,
            isOilLifeReset
                ? L(
                    "ნამდვილად გსურთ Oil Life-ის განულება?",
                    "Are you sure you want to reset the Oil Life?")
                : L(
                    $"{item.Description}\n\nაპი გაშვებამდე თავიდან შეამოწმებს ძაბვას, სიჩქარეს, ძრავის მდგომარეობას და exact ECU definition-ს. გაგრძელდეს?",
                    $"{item.Description}\n\nBefore execution the app will re-check voltage, speed, engine state and the exact ECU definition. Continue?"),
            isOilLifeReset ? L("განულება", "Reset") : L("გაგრძელება", "Continue"),
            L("გაუქმება", "Cancel"));

        if (!confirmed)
            return;

        try
        {
            IsBusy = true;

            using var cancellation =
                item.IsActiveTest
                    ? new CancellationTokenSource()
                    : null;

            _activeTestCancellation = cancellation;
            IsActiveTestRunning = item.IsActiveTest;

            ServiceResult = L(
                $"{item.Name} იწყება…",
                $"Starting {item.Name}…");

            var operationDefinition = _oemOperations.CurrentAvailability?
                .Operations
                .FirstOrDefault(candidate =>
                    candidate.Definition.OperationId.Equals(
                        item.OperationId,
                        StringComparison.OrdinalIgnoreCase))
                ?.Definition;

            var serviceValuesBefore = _lastServiceReadings.ToArray();

            var result = await _oemOperations.ExecuteAsync(
                _currentOemVehicle,
                item.OperationId,
                true,
                cancellation?.Token ?? CancellationToken.None);

            if (item.IsActiveTest &&
                result.Status == DiagnosticOperationStatus.Cancelled)
            {
                ServiceResult = L(
                    "Active Test გაჩერდა. Reviewed STOP ბრძანება უსაფრთხოების მიზნით გაიგზავნა.",
                    "Active Test stopped. The reviewed STOP command was sent for safety.");
            }
            else if (result.IsSuccess)
            {
                ServiceResult =
                    $"✓ {result.Message}" +
                    (string.IsNullOrWhiteSpace(result.BackupPath)
                        ? string.Empty
                        : L(
                            " · ECU backup შენახულია",
                            " · ECU backup saved"));

                if (!string.IsNullOrWhiteSpace(result.BackupPath))
                {
                    CanRestoreCodingBackup = true;

                    CodingBackupState = L(
                        "Backup: ახლახან შენახულია",
                        "Backup: just saved");
                }

                // Independent domain-level verification for service resets.
                // OemOperationService has already verified the reviewed UDS
                // response.  Now read the actual maintenance value again and
                // only call the value reset "confirmed" if the vehicle reports
                // the changed value itself.
                if (operationDefinition?.Type == OemOperationType.Service)
                {
                    await RefreshServiceValuesAsync(_currentOemVehicle);

                    var verification = _serviceResetVerifier.Verify(
                        operationDefinition,
                        serviceValuesBefore,
                        _lastServiceReadings);

                    ServiceResult = verification.State switch
                    {
                        ServiceResetVerificationState.ServiceValueVerified =>
                            L(
                                $"✓ RESET დადასტურდა ECU readback-ით. {verification.Detail}",
                                $"✓ RESET confirmed by ECU readback. {verification.Detail}"),

                        ServiceResetVerificationState.ProtocolVerifiedValueUnchanged =>
                            L(
                                $"✓ ECU-მ reviewed reset command დაადასტურა, მაგრამ წაკითხვადი Service მნიშვნელობა ჯერ არ შეცვლილა. აპი 100%-ს არ გააყალბებს. Ignition cycle-ის შემდეგ ხელახლა დააჭირე Check-ს. {verification.Detail}",
                                $"✓ The ECU confirmed the reviewed reset command, but the readable service value has not changed yet. The app will not fabricate 100%. Run Check again after an ignition cycle. {verification.Detail}"),

                        ServiceResetVerificationState.ProtocolVerifiedReadbackUnavailable =>
                            L(
                                $"✓ ECU-მ reviewed reset command დაადასტურა. დამოუკიდებელი Service-value readback ამ მანქანაზე მიუწვდომელია, ამიტომ დაფაზე/შემდეგ Check-ზე გადაამოწმე ახალი interval. {verification.Detail}",
                                $"✓ The ECU confirmed the reviewed reset command. Independent service-value readback is unavailable on this vehicle, so verify the new interval on the cluster/next Check. {verification.Detail}"),

                        _ => ServiceResult
                    };
                }
            }
            else
            {
                ServiceResult = L(
                    $"მოქმედება არ დასრულდა: {result.Message}",
                    $"Operation did not complete: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            ServiceResult = L(
                $"მოქმედება ვერ შესრულდა: {UserError.Describe(ex)}",
                $"Operation failed: {UserError.Describe(ex)}");
        }
        finally
        {
            IsActiveTestRunning = false;
            _activeTestCancellation = null;
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ServiceViewModel.ExecuteOemOperation");
        }
    }

    [RelayCommand]
    private void StopActiveTest()
    {
        if (!IsActiveTestRunning)
            return;

        ServiceResult = L(
            "Active Test ჩერდება — reviewed STOP ბრძანება გაიგზავნება…",
            "Stopping Active Test — the reviewed STOP command will be sent…");
        _activeTestCancellation?.Cancel();
    }

    [RelayCommand]
    private async Task RestoreCodingBackup()
    {
        try
        {
        if (IsBusy || _currentOemVehicle is null || !CanRestoreCodingBackup)
            return;

        var confirmed = await Shell.Current.DisplayAlertAsync(
            L("Original Coding Backup-ის აღდგენა", "Restore original coding backup"),
            L(
                "აღდგება ამ VIN-ის უახლესი original coding backup. აპი თავიდან გადაამოწმებს უსაფრთხოების პირობებს და ჩაწერის შემდეგ ECU-ს პასუხს verify-ს გაუკეთებს. გაგრძელდეს?",
                "The latest original coding backup for this VIN will be restored. Safety conditions will be re-checked and the ECU response will be verified after the write. Continue?"),
            L("აღდგენა", "Restore"),
            L("გაუქმება", "Cancel"));

        if (!confirmed)
            return;

        try
        {
            IsBusy = true;
            ServiceResult = L("Coding backup აღდგება…", "Restoring coding backup…");
            var result = await _oemOperations.RestoreLatestCodingBackupAsync(_currentOemVehicle, true);
            ServiceResult = result.IsSuccess
                ? $"✓ {result.Message}"
                : L($"Backup ვერ აღდგა: {result.Message}", $"Backup restore failed: {result.Message}");
        }
        catch (Exception ex)
        {
            ServiceResult = L(
                $"Backup-ის აღდგენა ვერ შესრულდა: {UserError.Describe(ex)}",
                $"Backup restore failed: {UserError.Describe(ex)}");
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ServiceViewModel.RestoreCodingBackup");
        }
    }

    private void SetErrorState(Exception exception)
    {
        _currentOemVehicle = null;
        IsGenericObdReady = false;
        ConnectionState = "ERROR";
        ConnectionStateColor = "#B3361F";
        ConnectionDetail = L(
            "კავშირის/ECU შესაძლებლობების შემოწმება ვერ დასრულდა.",
            "Connection/ECU capability check failed.");
        OemState = "ERROR";
        OemStateColor = "#B3361F";
        OemDetail = L(
            "უსაფრთხოების მიზნით არცერთი OEM ბრძანება არ გაიგზავნა.",
            "No OEM command was sent for safety.");
        ServiceResult = $"{L("შეცდომა", "Error")}: {UserError.Describe(exception)}";
        ClearOperationLists();
    }

    private static bool TryNumeric(string? value, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = new string(value
            .Where(character => char.IsDigit(character) || character is '.' or ',' or '-')
            .ToArray())
            .Replace(',', '.');

        return double.TryParse(
            normalized,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out result);
    }
}

/// <summary>One line of the "what happened after clearing" card.</summary>
public sealed record ClearResultRow(string Title, string Detail, Color DotColor);

public partial class ModuleScanViewModel : BaseViewModel
{
    private readonly DtcCatalogService _dtcCatalog;
    private readonly FullObdScanService _scan;
    private readonly ObdReportService _report;
    private readonly ReminderNotificationService _notifications;
    private readonly OemModuleDiscoveryService _oemModuleDiscovery;
    private readonly OemEcuCapabilityDetectionService _oemCapabilityDetection;
    private readonly OemEcuDtcService _oemDtcReader;
    private readonly OBDService _obd;
    private readonly LocalizationService _localization;
    private readonly OemFreezeFrameService _oemFreezeFrameReader;
    private readonly ModuleDtcScanService _moduleDtc;
    private readonly CarScanner.AI.AiDiagnosticService _ai;
    private readonly ProService _pro;
    private readonly ModuleLiveDataService _moduleLive;
    private readonly RepairCostSession _repairCosts;
    public ObservableCollection<ModuleScanItem> Modules { get; } = new();
    /// <summary>Engine Freeze Frame snapshot read during the scan (speed, rpm, temperature …).</summary>
    public ObservableCollection<FreezeFrameValue> FreezeFrameRows { get; } = new();
    [ObservableProperty] private bool hasFreezeFrame;
    [ObservableProperty] private string freezeFrameTitleText = "Freeze Frame";
    public bool HasFaults => ModuleFaults.Count > 0;
    public string FreezeFrameHintText => _localization.IsEnglish
        ? "Engine conditions at the moment the fault was stored"
        : "ძრავის მდგომარეობა შეცდომის დაფიქსირების მომენტში";
    public string RepairCostButtonText => _localization.IsEnglish ? "How much will it cost?" : "რამდენი დამიჯდება?";
    public ObservableCollection<ModuleFaultItem> ModuleFaults { get; } = new();
    public ObservableCollection<ModuleStatusRow> ModuleRows { get; } = new();
    [ObservableProperty] private bool hasModuleRows;
    [ObservableProperty] private string moduleSummaryText = string.Empty;
    public IReadOnlyList<string> DetailTabs { get; } = new[] { "Overview", "Faults", "Live Data", "Info" };
    [ObservableProperty] private string status = "";
    [ObservableProperty] private ObdScanReport? report;
    [ObservableProperty] private string adapterStatus = "";
    [ObservableProperty] private string scanState = "NOT SCANNED";
    [ObservableProperty] private string scanStateColor = "#94A3B8";
    [ObservableProperty] private string scanStateDetail = "დააჭირე SCAN-ს, რომ დადასტურდეს რომელი ECU პასუხობს რეალურად.";
    [ObservableProperty] private int scanProgress;
    [ObservableProperty] private double scanProgressFraction;
    [ObservableProperty] private string topologyText = "სკანირების შემდეგ გამოჩნდება მხოლოდ რეალურად პასუხმგებელი ECU-ები.";
    [ObservableProperty] private string topologySummary = "ჯერ არ შემოწმებულა";
    [ObservableProperty] private string topologyNotice = "OEM მოდულები მხოლოდ დადასტურებული მწარმოებლის პროფილისას გამოჩნდება.";
    [ObservableProperty] private ModuleScanItem? selectedModule;
    [ObservableProperty]
    private string scanButtonText = "სკანირების დაწყება";

    [ObservableProperty]
    private string clearDtcButtonText = "DTC კოდების წაშლა";
    [ObservableProperty] private bool hasSelectedModule;
    [ObservableProperty] private string selectedTab = "Overview";
    [ObservableProperty] private string selectedDetails = "აირჩიე აღმოჩენილი მოდული დეტალების სანახავად.";
    [ObservableProperty] private string freezeFrameText = "Freeze Frame მხოლოდ DTC-ის არსებობისას და ECU-ის მხარდაჭერისას გამოჩნდება.";
    [ObservableProperty] private string moduleFaultEmptyMessage = "სკანირების შემდეგ გამოჩნდება რეალურად წაკითხული DTC.";

    public bool IsNotBusy => !IsBusy;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsBusy))
            OnPropertyChanged(nameof(IsNotBusy));
    }

    public ModuleScanViewModel(
        FullObdScanService scan,
    ObdReportService report,
    ReminderNotificationService notifications,
    OemModuleDiscoveryService oemModuleDiscovery,
    OemEcuCapabilityDetectionService oemCapabilityDetection,
    OemEcuDtcService oemDtcReader,
    OemFreezeFrameService oemFreezeFrameReader,
    OBDService obd,
    LocalizationService localization,
    DtcCatalogService dtcCatalog,
    ModuleDtcScanService moduleDtc,
    CarScanner.AI.AiDiagnosticService ai,
    ProService pro,
    ModuleLiveDataService moduleLive,
    RepairCostSession repairCosts)
    {
        _repairCosts = repairCosts;
        _moduleDtc = moduleDtc;
        _ai = ai;
        _pro = pro;
        _moduleLive = moduleLive;
        _scan = scan;
        _report = report;
        _notifications = notifications;
        _oemModuleDiscovery = oemModuleDiscovery;
        _oemCapabilityDetection = oemCapabilityDetection;
        _oemDtcReader = oemDtcReader;
        _oemFreezeFrameReader = oemFreezeFrameReader;
        _obd = obd;
        _localization = localization;
        _dtcCatalog = dtcCatalog;

        UpdateLanguageTexts();
        ModuleFaults.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(FaultSummaryTitle));
            OnPropertyChanged(nameof(HasFaults));
        };

        Attach();

        Title = "შეცდომების სკანი";
    }

    // ModuleScanPage should call Attach() on appearing and Detach() on leaving.
    private bool _attached;

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(UpdateLanguageTexts);
    /// <summary>Big report title: "2 შეცდომა" / "DTC კოდები" before a scan.</summary>
    public string FaultSummaryTitle => ModuleFaults.Count switch
    {
        0 => _localization.IsEnglish ? "Trouble code scan" : "შეცდომების სკანი",
        1 => _localization.IsEnglish ? "1 fault" : "1 შეცდომა",
        var count => _localization.IsEnglish ? $"{count} faults" : $"{count} შეცდომა"
    };

    public string ModulesCaptionText => _localization.IsEnglish ? "Modules" : "მოდულები";

    private void UpdateLanguageTexts()
    {
        OnPropertyChanged(nameof(FaultSummaryTitle));
        OnPropertyChanged(nameof(ModulesCaptionText));
        OnPropertyChanged(nameof(FreezeFrameHintText));
        OnPropertyChanged(nameof(RepairCostButtonText));
        if (_localization.IsEnglish)
        {
            ScanButtonText = "Start Scan";
            ClearDtcButtonText = "Clear DTC Codes";
            ModuleLiveButtonText = "Module live data";
        }
        else
        {
            ScanButtonText = "სკანირების დაწყება";
            ClearDtcButtonText = "DTC კოდების წაშლა";
            ModuleLiveButtonText = "მოდულების ცოცხალი მონაცემები";
        }
    }

    [RelayCommand]
    public async Task Scan()
    {
        try
        {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            if (!_keepClearResults)
            {
                ClearResults.Clear();
                HasClearResults = false;
            }
            Modules.Clear();
            ModuleFaults.Clear();
            ModuleRows.Clear();
            FreezeFrameRows.Clear();
            HasFreezeFrame = false;
            HasModuleRows = false;
            ModuleSummaryText = string.Empty;
            SelectedModule = null;
            Report = null;
            ScanProgress = 0;
            ScanProgressFraction = 0;
            ScanState = "LOADING";
            ScanStateColor = "#60A5FA";
            ScanStateDetail = "Generic OBD-II შესაძლებლობები მოწმდება. მხოლოდ რეალურად პასუხმგებელი ECU დაემატება.";
            TopologyText = "Diagnostic path მოწმდება…";
            TopologySummary = "სკანირება მიმდინარეობს";
            TopologyNotice = "OEM ECU მოთხოვნა მხოლოდ verified definition-ის არსებობისას გაიგზავნება.";
            Status = "OBD-II სკანირება იწყება…";

            var progress = new Progress<ModuleScanProgress>(step =>
            {
                ScanProgress = step.Percent;
                ScanProgressFraction = Math.Clamp(step.Percent / 100d, 0d, 1d);
                Status = step.Stage;
                ScanStateDetail = step.Stage;
            });
            Report = await _scan.ScanAsync(progress);

            // FullObdScanService has already read this once. Do not issue a
            // second sequence of adapter commands after the scan completes.
            AdapterStatus = $"Adapter: {Report.AdapterName} · Protocol: {Report.Protocol} · Voltage: {Report.AdapterVoltage}";

            await AddFaultsAsync(
    Report.StoredCodes,
    "Stored",
    "OBD-ECU",
    "Generic OBD-II Powertrain ECU");

            await AddFaultsAsync(
                Report.PendingCodes,
                "Pending",
                "OBD-ECU",
                "Generic OBD-II Powertrain ECU");

            await AddFaultsAsync(
                Report.PermanentCodes,
                "Permanent",
                "OBD-ECU",
                "Generic OBD-II Powertrain ECU");

            AddModuleResults(Report);
            _moduleLive.LastVehicle = Report.Vehicle;
            await EnrichFaultsAsync();

            ModuleFaultEmptyMessage = Report.DtcServicesRead == 0
                ? "DTC მონაცემი ამ ECU-დან ვერ წაიკითხა. ეს არ ნიშნავს, რომ კოდი არ არის."
                : Report.HasFaults
                    ? ""
                    : Report.DtcServicesRead == Report.DtcServicesExpected
                        ? "აქტიური, Pending და Permanent შეცდომის კოდები არ აღმოჩნდა."
                        : $"DTC-ის ნაწილი წაკითხულია ({Report.DtcServicesRead}/{Report.DtcServicesExpected}); დარჩენილი სერვისები ECU-მ არ დააბრუნა.";

            FreezeFrameText = Report.FreezeFrames.Count == 0
                ? "Freeze Frame მონაცემი ამ სკანზე ECU-მ არ დააბრუნა."
                : string.Join("\n", Report.FreezeFrames.SelectMany(frame => frame.Values.Select(value => $"{value.Key}: {value.Value}")));
            ShowFreezeFrame(Report);

            if (Report.EcuResponded)
            {
                var readiness = Report.ReadinessRead && Report.Readiness is not null
                    ? $"Readiness: {Report.Readiness.StatusText}"
                    : "Readiness: ECU-მ მონაცემი არ დააბრუნა";
                var liveData = Report.SnapshotPidsRequested == 0
                    ? "Live Data snapshot: ამ ECU-მ თავსებადი სტანდარტული PID არ გამოაცხადა"
                    : Report.Sensors.Count == 0
                        ? $"Live Data snapshot: მოთხოვნილი {Report.SnapshotPidsRequested} PID-დან დეკოდირებადი პასუხი არ დაბრუნებულა"
                        : $"Live Data snapshot: {Report.Sensors.Count}/{Report.SnapshotPidsRequested} რეალური მნიშვნელობა · სულ მხარდაჭერილი PID: {Report.PidStats.Advertised}";
                var vehicleInfo = Report.VehicleInfoRead
                    ? $"VIN: {DisplayValue(Report.Vehicle.VIN)}"
                    : "VIN / Vehicle Info: მიუწვდომელია";
                var ecu = new ModuleScanItem
                {
                    Id = "OBD-ECU",
                    DefinitionId = "OBD-ECU",
                    Name = "Generic OBD-II Powertrain ECU",
                    Group = "Powertrain",
                    IsGenericObd = true,
                    IsDiscovered = true,
                    AvailabilityText = "AVAILABLE",
                    AvailabilityColor = "#4ADE80",
                    CommunicationText = "Communication OK — OBD პასუხი დადასტურებულია",
                    CommunicationColor = "#4ADE80",
                    StoredFaultCount = Report.StoredCodes.Count,
                    PendingFaultCount = Report.PendingCodes.Count,
                    PermanentFaultCount = Report.PermanentCodes.Count,
                    DtcServicesRead = Report.DtcServicesRead,
                    DtcServicesExpected = Report.DtcServicesExpected,
                    PendingDtcRead = Report.PendingDtcRead,
                    DataSummary = $"DTC services: {Report.DtcServicesRead}/{Report.DtcServicesExpected}\n{readiness}\n{liveData}\n{vehicleInfo}\nProtocol: {DisplayValue(Report.Protocol)}",
                    CapabilitiesSummary = string.Join("\n", new[]
                    {
                        Report.VehicleInfoRead ? "IDENTIFICATION.READ: Available" : "IDENTIFICATION.READ: Not Available",
                        Report.DtcServicesRead > 0 ? "DTC.READ: Available" : "DTC.READ: Not Available",
                        Report.Sensors.Count > 0 ? "DATA.READ: Available" : "DATA.READ: Not Available",
                        Report.FreezeFrames.Count > 0 ? "FREEZEFRAME.READ: Available" : "FREEZEFRAME.READ: Not Available"
                    }),
                    IdentificationCapability = Report.VehicleInfoRead ? "IDENTIFICATION.READ: Available" : "IDENTIFICATION.READ: Not Available",
                    DtcCapability = Report.DtcServicesRead > 0 ? "DTC.READ: Available" : "DTC.READ: Not Available",
                    LiveDataCapability = Report.Sensors.Count > 0 ? "DATA.READ: Available" : "DATA.READ: Not Available",
                    FreezeFrameCapability = Report.FreezeFrames.Count > 0 ? "FREEZEFRAME.READ: Available" : "FREEZEFRAME.READ: Not Available",
                    SourceText = "წყარო: Standard OBD-II Mode 01 / 03 / 07 / 0A",
                    LastScanAt = Report.FinishedAt
                };
                Modules.Add(ecu);
                SelectedModule = ecu;
                var genericTopology = VehicleNetworkTopologyBuilder.Build(true, Report.Protocol, null);
                TopologyText = genericTopology.TreeText;
                TopologySummary = genericTopology.Summary;
                ScanState = "AVAILABLE";
                ScanStateColor = "#4ADE80";
                ScanStateDetail = "1 რეალურად პასუხმგებელი Generic OBD-II ECU აღმოჩნდა. OEM მისამართის გარეშე ABS/SRS/BCM/TCM არ არის ნაჩვენები.";

                // OEM discovery never guesses module IDs. It runs only if a
                // verified model/address/read-only probe has been registered.
                try
                {
                    var oem = await _oemModuleDiscovery.DiscoverAsync(Report.Vehicle);
                    var capabilityDetection = await _oemCapabilityDetection.DetectAsync(Report.Vehicle, oem);
                    var oemDtcScan = await _oemDtcReader.ReadAsync(Report.Vehicle, oem, capabilityDetection);
                    var oemFreezeFrameScan = await _oemFreezeFrameReader.ReadAsync(Report.Vehicle, oemDtcScan);
                    var capabilitiesByDefinition = capabilityDetection.Ecus
                        .ToDictionary(profile => profile.DefinitionId, StringComparer.OrdinalIgnoreCase);
                    var dtcByDefinition = oemDtcScan.Ecus
                        .ToDictionary(result => result.DefinitionId, StringComparer.OrdinalIgnoreCase);
                    var freezeFramesByDefinition = oemFreezeFrameScan.Ecus
                        .ToDictionary(result => result.DefinitionId, StringComparer.OrdinalIgnoreCase);
                    foreach (var ecuDtc in oemDtcScan.Ecus)
                    {
                        foreach (var dtc in ecuDtc.Records)
                        {
                            ModuleFaults.Add(new ModuleFaultItem
                            {
                                DefinitionId = dtc.DefinitionId,
                                EcuName = dtc.EcuName,
                                Code = dtc.DisplayCode,
                                Description = dtc.Description,
                                Status = dtc.StatusText,
                                Severity = (dtc.StatusByte & (byte)UdsDtcStatus.WarningIndicatorRequested) != 0
                                    ? "Warning indicator requested"
                                    : "შესამოწმებელი"
                            });
                        }
                    }
                    foreach (var probe in oem.Probes)
                    {
                        capabilitiesByDefinition.TryGetValue(probe.DefinitionId, out var capabilityProfile);
                        dtcByDefinition.TryGetValue(probe.DefinitionId, out var ecuDtc);
                        freezeFramesByDefinition.TryGetValue(probe.DefinitionId, out var ecuFreezeFrames);
                        Modules.Add(new ModuleScanItem
                        {
                            Id = probe.EcuId,
                            DefinitionId = probe.DefinitionId,
                            Name = probe.Name,
                            Group = $"Manufacturer-specific · {probe.CanBus}",
                            IsDiscovered = probe.State is OemModuleProbeState.Available or OemModuleProbeState.RespondedLimited,
                            AvailabilityText = ProbeAvailability(probe.State),
                            AvailabilityColor = ProbeColor(probe.State),
                            CommunicationText = ProbeCommunication(probe.State),
                            CommunicationColor = ProbeColor(probe.State),
                            DtcServicesRead = ecuDtc?.State == OemDtcReadState.Available ? 1 : 0,
                            DtcServicesExpected = ecuDtc is null ? 0 : 1,
                            PendingDtcRead = false,
                            OemDtcSummary = OemDtcSummary(ecuDtc),
                            OemFreezeFrameSummary = OemFreezeFrameSummary(ecuFreezeFrames),
                            DataSummary = $"{probe.Detail}\nDiagnostic bus: {probe.CanBus}\nResponse time: {probe.ElapsedMilliseconds} ms\nDefinition: {probe.DefinitionId}",
                            CapabilitiesSummary = capabilityProfile?.Summary ?? "Capability probe არ შესრულებულა — ECU-ს დადებითი/შეზღუდული პასუხი არ დადასტურდა.",
                            IdentificationCapability = CapabilityStatus(capabilityProfile, "IDENTIFICATION.READ"),
                            DtcCapability = CapabilityStatus(capabilityProfile, "DTC.READ"),
                            LiveDataCapability = CapabilityStatus(capabilityProfile, "DATA.READ"),
                            FreezeFrameCapability = OemFreezeFrameCapability(ecuFreezeFrames, capabilityProfile),
                            SourceText = $"დადასტურებული წყარო: {probe.Source}",
                            LastScanAt = DateTime.Now
                        });
                    }

                    var topology = VehicleNetworkTopologyBuilder.Build(true, Report.Protocol, oem);
                    TopologyText = topology.TreeText;
                    TopologySummary = topology.Summary;
                    TopologyNotice = oem.Detail;
                    ScanStateDetail = oem.Probes.Count == 0
                        ? $"Generic ECU დადასტურდა. OEM discovery: {oem.Detail}"
                        : $"Generic ECU დადასტურდა; {oem.Probes.Count} verified OEM მისამართი შემოწმდა, დადებითი პასუხი: {oem.Modules.Count}.";
                    if (capabilityDetection.Ecus.Count > 0)
                        ScanStateDetail += $" Capability detection: {capabilityDetection.Detail}";
                    if (oemDtcScan.Ecus.Count > 0)
                        ScanStateDetail += $" OEM DTC: {oemDtcScan.Detail}";
                    if (oemFreezeFrameScan.Ecus.Count > 0)
                    {
                        ScanStateDetail += $" OEM Freeze Frame: {oemFreezeFrameScan.Detail}";
                        var snapshotText = BuildOemFreezeFrameText(oemFreezeFrameScan);
                        if (!string.IsNullOrWhiteSpace(snapshotText))
                            FreezeFrameText += $"\n\nOEM DTC SNAPSHOTS\n{snapshotText}";
                    }
                    if (ModuleFaults.Count > 0)
                        ModuleFaultEmptyMessage = string.Empty;
                }
                catch (Exception ex)
                {
                    // A failed OEM extension must never erase the completed
                    // generic scan or turn it into a false vehicle failure.
                    TopologyText += "\n  └── OEM discovery ვერ დასრულდა";
                    TopologyNotice = "OEM extension შეცდომით დასრულდა; Generic OBD შედეგი უცვლელად შენარჩუნდა.";
                    ScanStateDetail = "Generic ECU დადასტურდა. OEM discovery ვერ დასრულდა; Generic შედეგი შენახულია.";
                    System.Diagnostics.Debug.WriteLine($"OEM discovery failed: {ex}");
                    Status = $"{Status} OEM: {UserError.Describe(ex)}";
                }
            }
            else
            {
                var unavailableTopology = VehicleNetworkTopologyBuilder.Build(false, Report.Protocol, null);
                TopologyText = unavailableTopology.TreeText;
                TopologySummary = unavailableTopology.Summary;
                TopologyNotice = "ECU diagnostic path ვერ დადასტურდა.";
                ScanState = "NOT SUPPORTED";
                ScanStateColor = "#F59E0B";
                ScanStateDetail = "Adapter დაკავშირებულია, მაგრამ სტანდარტულ OBD ECU-ს პასუხი არ დაუბრუნებია. fake მოდული არ შექმნილა.";
                SelectedDetails = "რეალური მოდული ვერ დადასტურდა. შეამოწმე კონტაქტი ON, adapter, Bluetooth და ავტომობილის OBD-II მხარდაჭერა.";
                FreezeFrameText = "Freeze Frame არ შემოწმებულა — პასუხმგებელი OBD ECU ვერ დადასტურდა.";
            }

            if (Report.EcuResponded && (Report.Modules.Count > 0 || !string.IsNullOrWhiteSpace(Report.ModuleScanNote)))
                ScanStateDetail = ModuleSummaryText;

            SelectedTab = "Overview";
            if (SelectedModule is not null)
                BuildSelectedDetails();

            Status = !Report.EcuResponded
                ? "სკანირება დასრულდა — სტანდარტული OBD ECU პასუხი ვერ დადასტურდა."
                : Report.DtcServicesRead == 0
                    ? "სკანირება დასრულდა — ECU პასუხობს, მაგრამ DTC მონაცემი მიუწვდომელია."
                : Report.HasFaults
                    ? "სკანირება დასრულდა — შეცდომები ნაპოვნია."
                    : Report.DtcServicesRead == Report.DtcServicesExpected
                        ? "სკანირება დასრულდა — აქტიური DTC არ არის."
                        : "სკანირება დასრულდა — DTC მონაცემი ნაწილობრივ წაკითხულია.";
            await _notifications.MarkScanCompletedAsync();
            AddScanToReport();
        }
        catch (Exception ex)
        {
            Modules.Clear();
            ModuleFaults.Clear();
            SelectedModule = null;
            Report = null;
            ScanState = "ERROR";
            ScanStateColor = "#EF4444";
            ScanStateDetail = "OBD მოდულის სკანირება ვერ დასრულდა. მონაცემი არ არის გამოგონილი.";
            ModuleFaultEmptyMessage = "DTC კოდები ვერ წაიკითხა — სკანირება შეცდომით შეწყდა.";
            FreezeFrameText = "Freeze Frame არ იკითხება, რადგან სკანირება ვერ დასრულდა.";
            System.Diagnostics.Debug.WriteLine($"Full scan failed: {ex}");
            var scanError = UserError.Describe(ex);
            Status = string.Equals(scanError, UserError.Describe(null), StringComparison.Ordinal)
                ? "სრული სკანირება ვერ დასრულდა. შეამოწმე adapter და მანქანის კონტაქტი."
                : scanError;
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ModuleScanViewModel.Scan");
        }
    }

    public ObservableCollection<ClearResultRow> ClearResults { get; } = new();
    [ObservableProperty] private bool hasClearResults;
    [ObservableProperty] private string clearResultsTitle = string.Empty;
    private bool _keepClearResults;

    private string ClearL(string ka, string en) => _localization.IsEnglish ? en : ka;

    private const string EngineKey = "ENGINE";

    private static string FaultKey(ModuleFaultItem fault) =>
        fault.DefinitionId.StartsWith("MODULE-", StringComparison.Ordinal) ? fault.DefinitionId : EngineKey;

    private static string ModuleKey(ModuleDtcReport module) => $"MODULE-{module.TransmitCanId:X}";

    private Dictionary<string, HashSet<string>> SnapshotFaults() =>
        ModuleFaults
            .GroupBy(FaultKey)
            .ToDictionary(
                group => group.Key,
                group => group.Select(fault => fault.Code).ToHashSet(StringComparer.OrdinalIgnoreCase));

    private string NrcText(byte? nrc, string error) => nrc switch
    {
        0x22 => ClearL("პირობები არ სრულდება (ანთება ჩართეთ, მანქანა გააჩერეთ)", "conditions not correct (ignition on, car stopped)"),
        0x33 => ClearL("მოდული დაცვის კოდს ითხოვს", "module requires security access"),
        0x11 or 0x12 or 0x7F or 0x7E => ClearL("მოდული ამ გზით წაშლას არ უჭერს მხარს", "module does not support clearing this way"),
        0x31 => ClearL("მოდულმა მოთხოვნა არ მიიღო", "module rejected the request"),
        0x72 => ClearL("მოდულის მეხსიერების შეცდომა", "module memory failure"),
        null when error == "no answer" => ClearL("მოდულმა არ უპასუხა", "module did not answer"),
        null when error == "unsupported" => ClearL("ამ მოდულის წაშლა ჯერ მხარდაჭერილი არ არის", "clearing this module is not supported yet"),
        null => error,
        _ => $"NRC {nrc:X2}"
    };

    private async Task<double?> ReadSpeedForClearAsync()
    {
        try
        {
            var value = await _obd.ReadSensorAsync(0x0D);
            return value is not null && double.TryParse(value.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private async Task ClearDtc()
    {
        try
        {
            if (IsBusy)
                return;

            var isEnglish = _localization.IsEnglish;
            string ModuleTitle(ModuleDtcReport module) => isEnglish ? module.NameEn : module.NameKa;

            var modulesWithCodes = Report?.Modules
                .Where(module => module.Responded && module.Codes.Count > 0 && module.ModuleId != "ECM")
                .ToList() ?? new List<ModuleDtcReport>();

            // 1. What to clear.
            var clearEngine = true;
            var targets = new List<ModuleDtcReport>(modulesWithCodes);
            if (modulesWithCodes.Count > 0)
            {
                var allText = ClearL($"ყველა (ძრავი + {modulesWithCodes.Count} მოდული)", $"Everything (engine + {modulesWithCodes.Count} modules)");
                var engineText = ClearL("მხოლოდ ძრავი", "Engine only");
                var moduleTexts = modulesWithCodes
                    .Select(module => ClearL($"{ModuleTitle(module)} · {module.Codes.Count} კოდი", $"{ModuleTitle(module)} · {module.Codes.Count} codes"))
                    .ToList();
                var options = new[] { allText, engineText }.Concat(moduleTexts).ToArray();
                var picked = await Shell.Current.DisplayActionSheetAsync(
                    ClearL("რა წავშალო?", "What should be cleared?"), ClearL("გაუქმება", "Cancel"), null, options);
                if (string.IsNullOrEmpty(picked) || !options.Contains(picked))
                    return;

                if (picked == engineText)
                {
                    targets.Clear();
                }
                else if (picked != allText)
                {
                    clearEngine = false;
                    targets = new List<ModuleDtcReport> { modulesWithCodes[moduleTexts.IndexOf(picked)] };
                }
            }

            // 2. Safety: never while driving (ABS/ESP can switch off for a moment).
            var speed = await ReadSpeedForClearAsync();
            if (speed is > 0.5)
            {
                await Shell.Current.DisplayAlertAsync(
                    ClearL("ჯერ გააჩერეთ მანქანა", "Stop the car first"),
                    ClearL("მოძრაობისას შეცდომების წაშლა არ შეიძლება — ABS/ESP შეიძლება წამით გაითიშოს.",
                        "Codes cannot be cleared while driving — ABS/ESP may switch off for a moment."),
                    "OK");
                return;
            }

            // 3. Confirm with what will happen.
            var notes = new List<string>();
            if (clearEngine)
                notes.Add(ClearL("ძრავის მზადყოფნის მონიტორები განულდება — ტექდათვალიერებამდე ცოტა უნდა იაროთ.",
                    "Engine readiness monitors will reset — drive a while before an inspection."));
            if (targets.Any(module => module.ModuleId.Contains("SRS", StringComparison.OrdinalIgnoreCase) || module.NameEn.Contains("airbag", StringComparison.OrdinalIgnoreCase)))
                notes.Add(ClearL("Airbag: თუ ხარვეზი ჯერ კიდევ არსებობს, ნათურა ისევ აინთება — ეს უსაფრთხოების სისტემაა, შეამოწმეთ სერვისში.",
                    "Airbag: if the fault is still there the light comes back — this is a safety system, have it checked."));
            notes.Add(ClearL("წაშლის შემდეგ თავიდან დავასკანერებ და გაჩვენებთ, რა წაიშალა და რა დაბრუნდა.",
                "After clearing I will rescan and show what was cleared and what came back."));

            var scopeText = clearEngine
                ? (targets.Count > 0 ? ClearL($"ძრავი + {targets.Count} მოდული", $"engine + {targets.Count} modules") : ClearL("ძრავი", "engine"))
                : ModuleTitle(targets[0]);

            var confirmed = await Shell.Current.DisplayAlertAsync(
                ClearL("შეცდომების წაშლა", "Clear trouble codes"),
                ClearL($"წაიშლება: {scopeText}\n\n", $"Will clear: {scopeText}\n\n") + string.Join("\n\n", notes),
                ClearL("წაშლა", "Clear"),
                ClearL("გაუქმება", "Cancel"));
            if (!confirmed)
                return;

            var before = SnapshotFaults();
            ClearResults.Clear();
            HasClearResults = false;

            var engineAccepted = true;
            var engineMessage = string.Empty;
            IReadOnlyList<ModuleDtcScanService.ModuleClearResult> moduleResults = Array.Empty<ModuleDtcScanService.ModuleClearResult>();

            try
            {
                IsBusy = true;

                if (clearEngine)
                {
                    Status = ClearL("ძრავის კოდები იშლება...", "Clearing engine codes...");
                    var clear = await _obd.ClearDtcWithResultAsync();
                    engineAccepted = clear.IsSuccess;
                    engineMessage = clear.Message;
                }

                if (targets.Count > 0)
                {
                    Status = ClearL("მოდულების კოდები იშლება...", "Clearing module codes...");
                    moduleResults = await _moduleDtc.ClearDetailedAsync(targets, CancellationToken.None);
                }

                // Modules need a moment before they report again.
                await Task.Delay(1200);
            }
            catch (Exception ex)
            {
                Status = ClearL($"წაშლა ვერ შესრულდა: {UserError.Describe(ex)}", $"Clearing failed: {UserError.Describe(ex)}");
                await Shell.Current.DisplayAlertAsync(ClearL("შეცდომა", "Error"), Status, "OK");
                return;
            }
            finally
            {
                IsBusy = false;
            }

            // 4. Rescan and compare.
            _keepClearResults = true;
            try
            {
                await Scan();
            }
            finally
            {
                _keepClearResults = false;
            }

            var after = SnapshotFaults();
            var green = Color.FromArgb("#1E8A4C");
            var orange = Color.FromArgb("#D0701F");
            var red = Color.FromArgb("#C2372B");

            void AddRow(string title, string key, bool accepted, string refusal)
            {
                before.TryGetValue(key, out var was);
                after.TryGetValue(key, out var now);
                var wasCount = was?.Count ?? 0;
                var nowCount = now?.Count ?? 0;
                if (!accepted && nowCount > 0)
                {
                    ClearResults.Add(new ClearResultRow(title, ClearL($"✗ არ წაიშალა — {refusal}", $"✗ Not cleared — {refusal}"), red));
                }
                else if (nowCount == 0)
                {
                    ClearResults.Add(new ClearResultRow(title, ClearL($"✓ წაიშალა ({wasCount} → 0)", $"✓ Cleared ({wasCount} → 0)"), green));
                }
                else
                {
                    var back = string.Join(", ", now!.Take(4));
                    ClearResults.Add(new ClearResultRow(title,
                        ClearL($"⚠ {nowCount} კოდი დაბრუნდა: {back} — ხარვეზი ჯერ კიდევ არსებობს, საჭიროა შეკეთება.",
                            $"⚠ {nowCount} code(s) came back: {back} — the fault is still present and needs repair."),
                        orange));
                }
            }

            if (clearEngine)
                AddRow(ClearL("ძრავი", "Engine"), EngineKey, engineAccepted, engineMessage);
            foreach (var result in moduleResults)
                AddRow(ModuleTitle(result.Module), ModuleKey(result.Module), result.Confirmed, NrcText(result.Nrc, result.Error));

            ClearResultsTitle = ClearL("წაშლის შედეგი", "Clear result");
            HasClearResults = ClearResults.Count > 0;
            Status = ClearResults.All(row => row.DotColor == green)
                ? ClearL("ყველაფერი წაიშალა.", "Everything was cleared.")
                : ClearL("ნაწილი დაბრუნდა ან ვერ წაიშალა — იხილეთ შედეგი.", "Some codes came back or could not be cleared — see the result.");
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ModuleScanViewModel.ClearDtc");
        }
    }

    [RelayCommand]
    private void SelectModule(ModuleScanItem? module)
    {
        if (module is null) return;
        SelectedModule = module;
        BuildSelectedDetails();
    }

    partial void OnSelectedModuleChanged(ModuleScanItem? value)
    {
        HasSelectedModule = value is not null;
    }

    [RelayCommand]
    private void SelectTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab)) return;
        SelectedTab = tab;
        BuildSelectedDetails();
    }

    [RelayCommand]
    private Task OpenLiveData() => Shell.Current.GoToAsync(nameof(Views.LiveDataPage));

    /// <summary>ABS, airbag, body … results of the module scan.</summary>
    private void AddModuleResults(ObdScanReport report)
    {
        ModuleRows.Clear();
        var english = _localization.IsEnglish;
        var genericCodes = report.StoredCodes.Concat(report.PendingCodes).Concat(report.PermanentCodes)
            .Select(code => code.Code.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var responded = report.Modules.Where(module => module.Responded).ToList();
        var moduleFaultCount = 0;
        foreach (var module in responded)
        {
            var name = english ? module.NameEn : module.NameKa;
            var isEngine = module.ModuleId is "ECM";
            var codes = module.Codes
                .Where(code => !(isEngine && genericCodes.Contains(code.Code)))
                .ToList();

            foreach (var code in codes)
            {
                var title = english
                    ? (string.IsNullOrWhiteSpace(code.TitleEn) ? code.TitleKa : code.TitleEn)
                    : (string.IsNullOrWhiteSpace(code.TitleKa) ? code.TitleEn : code.TitleKa);
                if (string.IsNullOrWhiteSpace(title))
                    title = english
                        ? "No description in the database (manufacturer-specific code)."
                        : "ამ კოდის აღწერა ბაზაში ვერ მოიძებნა (მწარმოებლის სპეციფიკური კოდი).";

                if (code.Snapshot.Count > 0)
                {
                    var snapshot = string.Join(" · ", code.Snapshot.Select(value =>
                        $"{(english ? value.NameEn : value.NameKa)}: {value.Value}"));
                    title += english ? $"\nAt the moment of the fault: {snapshot}" : $"\nშეცდომის მომენტში: {snapshot}";
                }

                ModuleFaults.Add(new ModuleFaultItem
                {
                    DefinitionId = $"MODULE-{module.TransmitCanId:X}",
                    EcuName = name,
                    Code = code.DisplayCode,
                    Description = title,
                    Status = code.Active
                        ? (english ? "Active" : "აქტიური")
                        : code.Pending
                            ? (english ? "Pending" : "მოლოდინში")
                            : (english ? "Stored" : "შენახული"),
                    Severity = code.Active ? (english ? "Check" : "შესამოწმებელი") : (english ? "History" : "ისტორია")
                });
                moduleFaultCount++;
            }

            var total = module.Codes.Count;
            ModuleRows.Add(new ModuleStatusRow
            {
                Name = name,
                StatusText = module.Protocol == "—"
                    ? (english ? "answers, no code list" : "პასუხობს, კოდებს არ აჩვენებს")
                    : total == 0
                        ? (english ? "no faults" : "შეცდომა არ არის")
                        : (english ? $"{total} fault(s)" : $"{total} შეცდომა"),
                StatusColor = module.Protocol == "—"
                    ? Color.FromArgb("#94A3B8")
                    : total == 0 ? Color.FromArgb("#16A34A") : Color.FromArgb("#EF4444"),
                Detail = string.Join(" · ", new[]
                {
                    !string.IsNullOrWhiteSpace(module.EcuName) &&
                    !string.Equals(module.EcuName, name, StringComparison.OrdinalIgnoreCase)
                        ? module.EcuName
                        : null,
                    string.IsNullOrWhiteSpace(module.SoftwareVersion)
                        ? null
                        : (english ? "Software " : "პროგრამა ") + module.SoftwareVersion
                }.Where(item => !string.IsNullOrWhiteSpace(item)))
            });
        }

        HasModuleRows = ModuleRows.Count > 0;
        var summary = english
            ? $"Modules checked: {report.ModulesChecked} · answered: {responded.Count} · faults in modules: {responded.Sum(module => module.Codes.Count)}"
            : $"შემოწმდა {report.ModulesChecked} მისამართი · უპასუხა {responded.Count} მოდულმა · შეცდომები მოდულებში: {responded.Sum(module => module.Codes.Count)}";
        if (!string.IsNullOrWhiteSpace(report.ModuleScanNote))
            summary += "\n" + (english ? _localization.Translate(report.ModuleScanNote) : report.ModuleScanNote);
        ModuleSummaryText = summary;

        if (moduleFaultCount > 0)
            ModuleFaultEmptyMessage = string.Empty;
    }

    [ObservableProperty] private string moduleLiveButtonText = "მოდულების ცოცხალი მონაცემები";

    [RelayCommand]
    private Task OpenModuleLiveData() => Shell.Current.GoToAsync(nameof(Views.ModuleLiveDataPage));

    /// <summary>Adds possible causes and first checks to every shown fault.</summary>
    private async Task EnrichFaultsAsync()
    {
        var english = _localization.IsEnglish;
        foreach (var fault in ModuleFaults.ToList())
        {
            fault.CausesHeader = english ? "Possible causes" : "შესაძლო მიზეზები";
            fault.ChecksHeader = english ? "What to check" : "რა შევამოწმოთ";
            fault.AiButtonText = english ? "AI explanation" : "AI ახსნა";

            var code = fault.Code.Trim().ToUpperInvariant();
            if (code.Length < 5)
                continue;
            code = code[..5];

            DtcCatalogEntry? entry = null;
            try
            {
                entry = await _dtcCatalog.GetAsync(code);
            }
            catch
            {
                // Causes are optional; the code itself is already shown.
            }

            if (entry is null)
                continue;

            var causes = english ? entry.CausesEn : entry.CausesKa;
            if (causes.Count > 0)
                fault.CausesText = string.Join("\n", causes.Take(5).Select(cause => $"• {cause}"));

            if (!english && entry.ChecksKa.Count > 0)
                fault.ChecksText = string.Join("\n", entry.ChecksKa.Select((check, index) => $"{index + 1}. {check}"));
        }
    }

    /// <summary>Asks the AI to explain one fault code in plain words (PRO).</summary>
    [RelayCommand]
    private async Task ExplainFault(ModuleFaultItem? fault)
    {
        try
        {
        if (fault is null || fault.IsAiBusy)
            return;

        var english = _localization.IsEnglish;
        if (!await ProGate.EnsureAsync(_pro, "კოდის AI ახსნა", "AI code explanation"))
            return;

        var code = fault.Code.Trim().ToUpperInvariant();
        fault.IsAiBusy = true;
        fault.AiExplanation = english ? "AI is analyzing…" : "AI აანალიზებს…";
        try
        {
            var errorCode = new ErrorCode
            {
                Code = code.Length >= 5 ? code[..5] : code,
                TitleKa = english ? string.Empty : fault.Description,
                TitleEn = english ? fault.Description : string.Empty,
                DescriptionEn = fault.Description,
                PossibleCausesKa = english ? string.Empty : fault.CausesText,
                PossibleCausesEn = english ? fault.CausesText : string.Empty,
                Status = fault.Status
            };

            var question = english
                ? $"Explain fault code {code} from the \"{fault.EcuName}\" module in simple words: what it means, the most likely causes for this car, whether it is safe to drive, and what to check first. Keep it short and practical."
                : $"ახსენი შეცდომის კოდი {code} („{fault.EcuName}“ მოდულიდან) მარტივი ქართულით: რას ნიშნავს, ამ მანქანისთვის ყველაზე სავარაუდო მიზეზები, შეიძლება თუ არა მგზავრობა და რა შევამოწმოთ პირველ რიგში. მოკლედ და პრაქტიკულად.";

            var reply = await _ai.ChatAsync(
                Report?.Vehicle ?? new VehicleInfo(),
                new[] { errorCode },
                Array.Empty<SensorValue>(),
                Array.Empty<string>(),
                question);

            fault.AiExplanation = string.IsNullOrWhiteSpace(reply)
                ? (english ? "The AI did not return an answer. Try again." : "AI-მ პასუხი არ დააბრუნა. სცადე თავიდან.")
                : reply.Trim();
        }
        catch
        {
            fault.AiExplanation = english
                ? "Could not get the AI answer. Check the internet connection and try again."
                : "AI პასუხი ვერ მივიღეთ. შეამოწმე ინტერნეტი და სცადე თავიდან.";
        }
        finally
        {
            fault.IsAiBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ModuleScanViewModel.ExplainFault");
        }
    }

    private async Task AddFaultsAsync(
    IEnumerable<DiagnosticCode> codes,
    string status,
    string definitionId,
    string ecuName)
    {
        foreach (var code in codes)
        {
            var normalizedCode =
                code.Code.Trim().ToUpperInvariant();
            if (ModuleFaults.Any(existing => existing.DefinitionId == definitionId &&
                                             existing.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase) &&
                                             existing.Status == FaultStatusText(status)))
                continue;

            DtcCatalogEntry? catalog = null;

            try
            {
                catalog = await _dtcCatalog.GetAsync(normalizedCode);
            }
            catch
            {
                // თუ catalog ვერ ჩაიტვირთა,
                // ქვემოთ ძველ ECU description-ს გამოვიყენებთ.
            }

            string description;

            if (catalog != null)
            {
                if (_localization.IsEnglish)
                {
                    description =
                        !string.IsNullOrWhiteSpace(catalog.TitleEn)
                            ? catalog.TitleEn
                            : !string.IsNullOrWhiteSpace(catalog.DescriptionEn)
                                ? catalog.DescriptionEn
                                : code.Description;
                }
                else
                {
                    description =
                        !string.IsNullOrWhiteSpace(catalog.TitleKa)
                            ? catalog.TitleKa
                            : !string.IsNullOrWhiteSpace(catalog.DescriptionKa)
                                ? catalog.DescriptionKa
                                : !string.IsNullOrWhiteSpace(catalog.TitleEn)
                                    ? catalog.TitleEn
                                    : code.Description;
                }
            }
            else
            {
                description = _localization.IsEnglish
                    ? code.Description
                    : "ამ კოდის აღწერა ბაზაში ვერ მოიძებნა (შესაძლოა მწარმოებლის სპეციფიკური კოდია).";
            }

            ModuleFaults.Add(new ModuleFaultItem
            {
                DefinitionId = definitionId,
                EcuName = definitionId == "OBD-ECU" ? EngineEcuText() : ecuName,
                Code = normalizedCode,
                Description = description,
                Status = FaultStatusText(status),
                Severity =
                    status == "Pending"
                        ? "დაკვირვება საჭიროა"
                        : "შესამოწმებელი"
            });
        }
    }

    private string EngineEcuText() => _localization.IsEnglish ? "Engine (ECU)" : "ძრავი (ECU)";

    /// <summary>The finished scan becomes the "full scan" part of the combined report.</summary>
    private void AddScanToReport()
    {
        if (Report is null || !Report.EcuResponded)
            return;

        var english = _localization.IsEnglish;
        var section = new CarScanner.Services.Reports.ReportSectionItem
        {
            Kind = "scan",
            Icon = ModuleFaults.Count == 0 ? "✓" : "⚠",
            Title = english ? "Full scan" : "სრული სკანი",
            Status = ModuleFaults.Count == 0
                ? CarScanner.Services.Reports.ReportStatus.Ok
                : ModuleFaults.Count >= 4 ? CarScanner.Services.Reports.ReportStatus.Bad : CarScanner.Services.Reports.ReportStatus.Warning,
            Summary = string.Join(" · ", new[]
            {
                ModuleFaults.Count == 0 ? (english ? "No fault codes" : "შეცდომა არ არის") : (english ? $"{ModuleFaults.Count} fault codes" : $"{ModuleFaults.Count} შეცდომა"),
                ModuleRows.Count > 0 ? (english ? $"{ModuleRows.Count + 1} modules" : $"{ModuleRows.Count + 1} მოდული") : string.Empty,
                Report.ReadinessRead && Report.Readiness is not null ? Report.Readiness.StatusText : string.Empty,
                FreezeFrameRows.Count > 0 ? "Freeze Frame" : string.Empty
            }.Where(part => !string.IsNullOrWhiteSpace(part)))
        };

        foreach (var fault in ModuleFaults)
            section.Lines.Add(new() { Label = fault.Code, Value = $"{fault.Description} · {fault.MetaText}" });
        if (Report.ReadinessRead && Report.Readiness is not null)
            section.Lines.Add(new() { Label = english ? "Readiness" : "მზადყოფნა", Value = Report.Readiness.StatusText });
        foreach (var row in FreezeFrameRows)
            section.Lines.Add(new() { Label = $"Freeze Frame · {row.Label}", Value = row.Value });
        foreach (var module in ModuleRows)
            section.Lines.Add(new() { Label = module.Name, Value = module.StatusText });

        CarScanner.Services.Reports.VehicleReportService.Instance?.AddOrReplace(section, Report);
    }

    /// <summary>Stored / Pending / Permanent in plain words.</summary>
    private string FaultStatusText(string status) => (status, _localization.IsEnglish) switch
    {
        ("Stored", false) => "შენახული",
        ("Pending", false) => "მოლოდინში (ჯერ არ დადასტურებულა)",
        ("Permanent", false) => "მუდმივი (წაშლით არ იშლება)",
        ("Pending", true) => "Pending (not confirmed yet)",
        ("Permanent", true) => "Permanent (cannot be cleared)",
        _ => status
    };

    /// <summary>Freeze Frame from the scan, shown as label / value rows.</summary>
    private void ShowFreezeFrame(ObdScanReport report)
    {
        FreezeFrameRows.Clear();
        var frame = report.FreezeFrames.FirstOrDefault(item => item.Values.Count > 0);
        if (frame is null)
        {
            HasFreezeFrame = false;
            return;
        }

        foreach (var value in frame.Values)
            FreezeFrameRows.Add(new FreezeFrameValue { Label = value.Key, Value = value.Value });
        FreezeFrameTitleText = string.IsNullOrWhiteSpace(frame.TriggerCode)
            ? "Freeze Frame"
            : $"Freeze Frame · {frame.TriggerCode}";
        HasFreezeFrame = true;
    }

    [RelayCommand]
    private async Task OpenCoding()
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(Views.CodingPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ModuleScanViewModel.OpenCoding");
        }
    }

    [RelayCommand]
    private async Task OpenRepairCosts()
    {
        try
        {
        if (ModuleFaults.Count == 0)
            return;

        var codes = ModuleFaults
            .GroupBy(fault => fault.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(fault => new ErrorCode
            {
                Code = fault.Code,
                TitleKa = fault.Description,
                DescriptionKa = fault.Description,
                PossibleCausesKa = fault.CausesText,
                ChecksKa = fault.ChecksText
            })
            .ToList();
        _repairCosts.Set(Report?.Vehicle, codes);
        await Shell.Current.GoToAsync(nameof(Views.RepairCostPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ModuleScanViewModel.OpenRepairCosts");
        }
    }

    private void BuildSelectedDetails()
    {
        if (SelectedModule is null)
        {
            SelectedDetails = "აირჩიე აღმოჩენილი მოდული.";
            return;
        }

        SelectedDetails = SelectedTab switch
        {
            "Overview" => $"{SelectedModule.Name}\n{SelectedModule.CommunicationText}\n{SelectedModule.FaultText}\n{SelectedModule.PendingText}\n{SelectedModule.DataSummary}\n{SelectedModule.SourceText}\n{SelectedModule.LastScanText}",
            "Faults" => !SelectedModule.IsGenericObd
                ? $"{SelectedModule.OemDtcSummary}\n{SelectedModule.DtcCapability}\n" +
                  (ModuleFaults.Any(fault => fault.DefinitionId.Equals(SelectedModule.DefinitionId, StringComparison.OrdinalIgnoreCase))
                      ? "ქვემოთ ნაჩვენებია ამ OEM ECU-დან რეალურად წაკითხული კოდები."
                      : "ამ OEM ECU-სთვის საჩვენებელი DTC ჩანაწერი არ არის.")
                : Report?.DtcServicesRead == 0
                ? "DTC მონაცემი ამ ECU-დან ვერ წაიკითხა; კოდების არარსებობა არ არის დადასტურებული."
                : ModuleFaults.Count == 0
                    ? Report?.DtcServicesRead == Report?.DtcServicesExpected
                        ? "აქტიური, შენახული, Pending ან Permanent კოდი არ აღმოჩნდა."
                        : $"კოდი წაკითხულ სერვისებში არ აღმოჩნდა, მაგრამ DTC წაკითხვა ნაწილობრივ დასრულდა ({Report?.DtcServicesRead}/{Report?.DtcServicesExpected})."
                : "ქვემოთ ნაჩვენებია ამ სკანის დროს ECM-დან რეალურად წაკითხული კოდები.",
            "Live Data" => !SelectedModule.IsGenericObd
                ? SelectedModule.LiveDataCapability
                : Report?.Sensors.Count > 0
                ? $"ამ სკანზე {Report.Sensors.Count} რეალური PID მნიშვნელობა წაკითხულია. Live Data Center-ში იხილავ პერიოდულ განახლებას მხოლოდ მხარდაჭერილი PID-ებისთვის."
                : "ამ სკანზე ECU-მ დეკოდირებადი live მნიშვნელობა არ დააბრუნა.",
            "Info" => $"{SelectedModule.IdentificationCapability}\n{SelectedModule.FreezeFrameCapability}\n{SelectedModule.OemFreezeFrameSummary}\n{SelectedModule.DataSummary}\n{SelectedModule.SourceText}\n{SelectedModule.LastScanText}",
            _ => "მოდულის ინფორმაცია მიუწვდომელია."
        };
    }

    private static string DisplayValue(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? "მიუწვდომელია"
            : value;

    private static string ProbeAvailability(OemModuleProbeState state) => state switch
    {
        OemModuleProbeState.Available => "AVAILABLE",
        OemModuleProbeState.RespondedLimited => "RESPONDED / LIMITED",
        OemModuleProbeState.NoResponse => "NO RESPONSE",
        OemModuleProbeState.TransportUnsupported => "NOT SUPPORTED",
        OemModuleProbeState.MalformedResponse => "INVALID RESPONSE",
        _ => "ERROR"
    };

    private static string ProbeColor(OemModuleProbeState state) => state switch
    {
        OemModuleProbeState.Available => "#4ADE80",
        OemModuleProbeState.RespondedLimited => "#F59E0B",
        OemModuleProbeState.NoResponse => "#94A3B8",
        OemModuleProbeState.TransportUnsupported => "#94A3B8",
        _ => "#EF4444"
    };

    private static string ProbeCommunication(OemModuleProbeState state) => state switch
    {
        OemModuleProbeState.Available => "Communication OK — დადებითი read-only პასუხი",
        OemModuleProbeState.RespondedLimited => "ECU პასუხობს — მოთხოვნა უარყოფილია/შეზღუდულია",
        OemModuleProbeState.NoResponse => "მითითებულ მისამართზე პასუხი არ დაბრუნდა",
        OemModuleProbeState.TransportUnsupported => "Adapter/transport ამ მოთხოვნას არ უჭერს მხარს",
        OemModuleProbeState.MalformedResponse => "პასუხი მიღებულია, მაგრამ ვერ დადასტურდა",
        _ => "ECU probe შეცდომით დასრულდა"
    };

    private static string CapabilityStatus(OemEcuCapabilityProfile? profile, string code) =>
        profile?.StatusFor(code) ?? $"{code}: Not Checked";

    private static string OemDtcSummary(OemEcuDtcResult? result) => result switch
    {
        null => "DTC: Not Checked",
        { State: OemDtcReadState.Available, Records.Count: 0 } => "OEM DTC კოდი არ აღმოჩნდა",
        { State: OemDtcReadState.Available } => $"OEM DTC: {result.Records.Count} კოდი",
        { State: OemDtcReadState.NotSupported } => "OEM DTC: Not Supported",
        { State: OemDtcReadState.NoResponse } => "OEM DTC: No Response",
        { State: OemDtcReadState.InvalidResponse } => "OEM DTC: Invalid Response",
        _ => "OEM DTC: Error"
    };

    private static string OemFreezeFrameSummary(OemEcuFreezeFrameResult? result) => result switch
    {
        null => "Freeze Frame: Not Configured",
        { State: OemFreezeFrameReadState.Available, Records.Count: 0 } => "Freeze Frame მოთხოვნა შესრულდა · snapshot არ არის",
        { State: OemFreezeFrameReadState.Available } => $"Freeze Frame: {result.Records.Count} snapshot",
        { State: OemFreezeFrameReadState.NotApplicable } => $"Freeze Frame: Not Applicable · {result.Detail}",
        { State: OemFreezeFrameReadState.NotSupported } => "Freeze Frame: Not Supported",
        { State: OemFreezeFrameReadState.NoResponse } => "Freeze Frame: No Response",
        { State: OemFreezeFrameReadState.InvalidResponse } => "Freeze Frame: Invalid Response",
        _ => "Freeze Frame: Error"
    };

    private static string OemFreezeFrameCapability(
        OemEcuFreezeFrameResult? result,
        OemEcuCapabilityProfile? capabilityProfile) => result switch
        {
            { State: OemFreezeFrameReadState.Available } =>
                "FREEZEFRAME.READ: Available — ECU snapshot პასუხი დადასტურებულია",
            { State: OemFreezeFrameReadState.NotApplicable } =>
                "FREEZEFRAME.READ: Conditional — რეალური DTC snapshot-ისთვის DTC აუცილებელია",
            { State: OemFreezeFrameReadState.NotSupported } => "FREEZEFRAME.READ: Not Supported",
            { State: OemFreezeFrameReadState.NoResponse } => "FREEZEFRAME.READ: No Response",
            { State: OemFreezeFrameReadState.InvalidResponse } => "FREEZEFRAME.READ: Invalid Response",
            _ => CapabilityStatus(capabilityProfile, "FREEZEFRAME.READ")
        };

    private static string BuildOemFreezeFrameText(OemFreezeFrameScanResult scan) => string.Join(
        "\n",
        scan.Ecus.SelectMany(ecu => ecu.Records.Select(record =>
            $"{ecu.EcuName} · DTC 0x{record.RawDtcHex} · Record 0x{record.SnapshotRecordNumber:X2}\n" +
            (record.Values.Count == 0
                ? "Snapshot მნიშვნელობები არ დაბრუნებულა."
                : string.Join(" · ", record.Values.Select(value => $"{value.Label}: {value.DisplayValue}"))))));
    [RelayCommand] public async Task Export() {
        try
        { if (Report == null) return; try { var path = await _report.ExportAsync(Report, "txt"); await Share.Default.RequestAsync(new ShareFileRequest { Title = "OBD-II Report", File = new ShareFile(path) }); } catch (Exception ex) { Status = UserError.Describe(ex); }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ModuleScanViewModel.Export");
        } }
    [RelayCommand] public async Task ExportJson() {
        try
        { if (Report == null) return; try { var path = await _report.ExportAsync(Report, "json"); await Share.Default.RequestAsync(new ShareFileRequest { Title = "OBD-II JSON Report", File = new ShareFile(path) }); } catch (Exception ex) { Status = UserError.Describe(ex); }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ModuleScanViewModel.ExportJson");
        } }
    [RelayCommand] public async Task ExportCsv() {
        try
        { if (Report == null) return; try { var path = await _report.ExportAsync(Report, "csv"); await Share.Default.RequestAsync(new ShareFileRequest { Title = "OBD-II CSV Report", File = new ShareFile(path) }); } catch (Exception ex) { Status = UserError.Describe(ex); }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ModuleScanViewModel.ExportCsv");
        } }
    [RelayCommand] public async Task ExportPdf() {
        try
        { if (Report == null) return; try { var path = await _report.ExportPdfAsync(Report); await Share.Default.RequestAsync(new ShareFileRequest { Title = "OBD-II PDF Report", File = new ShareFile(path) }); } catch (Exception ex) { Status = UserError.Describe(ex); }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ModuleScanViewModel.ExportPdf");
        } }
}

/// <summary>One service reminder row on the Service page.</summary>
public sealed class ServiceReminderRow
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public string ValueColor { get; init; } = "#1F8A4C";
    public ServiceMetricReading? ResetReading { get; init; }
    public string ResetButtonText { get; init; } = string.Empty;
    public bool CanReset => ResetReading is not null;
    public string ManualHint { get; init; } = string.Empty;
    public bool HasManualHint => !CanReset && !string.IsNullOrWhiteSpace(ManualHint);
}
