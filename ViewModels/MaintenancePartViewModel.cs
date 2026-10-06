using System.Collections.ObjectModel;
using System.Globalization;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CarScanner.Services.Maintenance;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>One service item: status, interval, history and "changed" records.</summary>
[QueryProperty(nameof(PartKey), "part")]
public partial class MaintenancePartViewModel : BaseViewModel
{
    private readonly MaintenanceTrackerService _tracker;
    private readonly LocalizationService _localization;
    private readonly ServiceProcedureCatalogService _procedures;
    private PartId _part = PartId.Oil;

    [ObservableProperty] private string partKey = string.Empty;
    [ObservableProperty] private string partName = string.Empty;
    [ObservableProperty] private string stateText = "—";
    [ObservableProperty] private Color stateColor = MaintenanceText.UnknownColor;
    [ObservableProperty] private string lastText = string.Empty;
    [ObservableProperty] private string intervalKmText = string.Empty;
    [ObservableProperty] private string intervalMonthsText = string.Empty;
    [ObservableProperty] private string standardText = string.Empty;
    [ObservableProperty] private string todayButtonText = string.Empty;
    [ObservableProperty] private bool showCustom;
    [ObservableProperty] private DateTime? customDate = DateTime.Today;
    [ObservableProperty] private string customKmText = string.Empty;
    [ObservableProperty] private bool hasHistory;
    [ObservableProperty] private bool hasInstruction;
    [ObservableProperty] private string instructionText = string.Empty;
    [ObservableProperty] private bool hasMessage;
    [ObservableProperty] private string messageText = string.Empty;

    public ObservableCollection<string> History { get; } = new();

    public MaintenancePartViewModel(
        MaintenanceTrackerService tracker,
        LocalizationService localization,
        ServiceProcedureCatalogService procedures)
    {
        _tracker = tracker;
        _localization = localization;
        _procedures = procedures;
    }

    private bool English => _localization.IsEnglish;
    private string L(string ka, string en) => English ? en : ka;

    public string IntervalCaption => L("ინტერვალი", "Interval");
    public string KmCaption => MaintenanceText.DistanceUnit;
    public string MonthsCaption => L("თვე", "months");
    public string SaveIntervalText => L("ინტერვალის შენახვა", "Save interval");
    public string RestoreText => L("სტანდარტზე დაბრუნება", "Back to standard");
    public string OtherDateText => L("სხვა თარიღი ან გარბენი", "Another date or odometer");
    public string DateCaption => L("თარიღი", "Date");
    public string KmAtChangeCaption => L($"გარბენი ({MaintenanceText.DistanceUnit})", $"Odometer ({MaintenanceText.DistanceUnit})");
    public string SaveRecordText => L("ჩანაწერის შენახვა", "Save record");
    public string HistoryCaption => L("ისტორია", "History");
    public string RemoveLastText => L("ბოლო ჩანაწერის წაშლა", "Delete last record");
    public string InstructionCaption => L("დაფაზე შეხსენების ჩაქრობა", "Reset the dashboard reminder");
    public string InstructionNote => L("ხელით, დაფიდან · ECU-ში არაფერი იწერება", "By hand on the dashboard · nothing is written to the ECU");

    partial void OnPartKeyChanged(string value)
    {
        if (Enum.TryParse<PartId>(value, true, out var part))
            _part = part;
        Reload();
        _ = LoadInstructionAsync();
    }

    private void Reload()
    {
        var key = _tracker.CurrentKey;
        var data = _tracker.Load(key);
        var definition = MaintenanceSchedule.Get(_part);
        var interval = _tracker.IntervalFor(data, _part);
        var standard = MaintenanceSchedule.DefaultInterval(_tracker.BrandGroup, _part);
        var status = _tracker.StatusOf(data, _part);

        PartName = English ? definition.NameEn : definition.NameKa;
        Title = PartName;
        StateColor = MaintenanceText.ColorOf(status.State);
        StateText = status.State switch
        {
            PartState.Unknown => L("ჯერ არ არის მითითებული", "Not set yet"),
            PartState.Overdue => L("ვადა გავიდა", "Overdue"),
            _ => L("დარჩა: ", "Left: ") + MaintenanceText.ValueText(status, English)
        };
        LastText = MaintenanceText.LastText(status, English);

        IntervalKmText = interval.Km > 0 ? MaintenanceText.IntervalDisplay(interval.Km).ToString("0", CultureInfo.InvariantCulture) : string.Empty;
        IntervalMonthsText = interval.Months > 0 ? interval.Months.ToString(CultureInfo.InvariantCulture) : string.Empty;
        var brand = MaintenanceSchedule.BrandGroupName(_tracker.BrandGroup);
        StandardText = brand.Length > 0
            ? L($"{brand}-ის სტანდარტი: {MaintenanceText.IntervalText(standard, false)}", $"{brand} standard: {MaintenanceText.IntervalText(standard, true)}")
            : L($"ზოგადი სტანდარტი: {MaintenanceText.IntervalText(standard, false)}", $"General standard: {MaintenanceText.IntervalText(standard, true)}");

        var odometer = MaintenanceTrackerService.Odometer(data);
        TodayButtonText = odometer is null
            ? L("დღეს შევიცვალე", "Changed today")
            : L($"დღეს შევიცვალე · {MaintenanceText.Km(odometer.Km)}", $"Changed today · {MaintenanceText.Km(odometer.Km)}");
        if (string.IsNullOrWhiteSpace(CustomKmText) && odometer is not null)
            CustomKmText = odometer.Km.ToString("0", CultureInfo.InvariantCulture);

        History.Clear();
        if (data.Parts.TryGetValue(_part.ToString(), out var part))
        {
            foreach (var record in part.History
                         .OrderByDescending(item => item.DateUtc ?? DateTime.MinValue)
                         .ThenByDescending(item => item.Km ?? 0))
            {
                var date = record.DateUtc?.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "—";
                var km = record.Km is { } value ? MaintenanceText.Km(value) : "—";
                History.Add($"{date}   ·   {km}");
            }
        }

        HasHistory = History.Count > 0;
    }

    private async Task LoadInstructionAsync()
    {
        HasInstruction = false;
        InstructionText = string.Empty;
        if (_part != PartId.Oil)
            return;

        try
        {
            var procedures = await _procedures.GetForVehicleAsync(_tracker.CurrentVehicle, null, CancellationToken.None);
            var procedure = procedures
                .Where(item => item.ExecutionType == ServiceProcedureExecutionType.Manual)
                .OrderByDescending(item => item.Category.Equals("Oil Service", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(item => item.NameEn.Contains("Oil", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (procedure is null || procedure.Steps.Count == 0)
                return;

            InstructionText = string.Join(
                Environment.NewLine,
                procedure.Steps.OrderBy(step => step.Order).Select(step => $"{step.Order}. {(English ? step.TextEn : step.TextKa)}"));
            HasInstruction = InstructionText.Length > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Maintenance instruction load failed: {ex.Message}");
        }
    }

    private void Say(string text)
    {
        MessageText = text;
        HasMessage = text.Length > 0;
    }

    [RelayCommand]
    private void MarkToday()
    {
        var key = _tracker.CurrentKey;
        var odometer = MaintenanceTrackerService.Odometer(_tracker.Load(key));
        _tracker.AddRecord(key, _part, DateTime.Now, odometer?.Km);
        Say(L("შენახულია.", "Saved."));
        Reload();
    }

    [RelayCommand]
    private void ToggleCustom() => ShowCustom = !ShowCustom;

    [RelayCommand]
    private void SaveCustom()
    {
        var date = (CustomDate ?? DateTime.Today).Date;
        if (date > DateTime.Today)
        {
            Say(L("თარიღი მომავალში ვერ იქნება.", "The date cannot be in the future."));
            return;
        }

        double? km = null;
        if (!string.IsNullOrWhiteSpace(CustomKmText))
        {
            var digits = new string(CustomKmText.Where(char.IsDigit).ToArray());
            if (!double.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value is <= 0 or >= 2_000_000)
            {
                Say(L("გარბენი არასწორია.", "The odometer value is not valid."));
                return;
            }

            km = MaintenanceText.FromDisplay(value);
        }

        _tracker.AddRecord(_tracker.CurrentKey, _part, date.AddHours(12), km);
        ShowCustom = false;
        Say(L("შენახულია.", "Saved."));
        Reload();
    }

    [RelayCommand]
    private void SaveInterval()
    {
        static int? Parse(string text)
        {
            var digits = new string((text ?? string.Empty).Where(char.IsDigit).ToArray());
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
        }

        var shown = Parse(IntervalKmText);
        int? km = shown is { } value ? (int)Math.Round(MaintenanceText.FromDisplay(value)) : null;
        var months = Parse(IntervalMonthsText);
        if (km is null && months is null)
        {
            Say(L($"ჩაწერე {MaintenanceText.DistanceUnit} ან თვეები.", $"Enter {MaintenanceText.DistanceUnit} or months."));
            return;
        }

        if (km is > 500_000 || months is > 240)
        {
            Say(L("ინტერვალი ძალიან დიდია.", "The interval is too large."));
            return;
        }

        _tracker.SetInterval(_tracker.CurrentKey, _part, km ?? 0, months ?? 0);
        Say(L("ინტერვალი შენახულია.", "Interval saved."));
        Reload();
    }

    [RelayCommand]
    private void RestoreStandard()
    {
        var standard = MaintenanceSchedule.DefaultInterval(_tracker.BrandGroup, _part);
        _tracker.SetInterval(_tracker.CurrentKey, _part, standard.Km, standard.Months);
        Say(L("სტანდარტული ინტერვალი დაბრუნდა.", "Standard interval restored."));
        Reload();
    }

    [RelayCommand]
    private async Task RemoveLast()
    {
        try
        {
        var confirm = await Shell.Current.DisplayAlertAsync(
            PartName,
            L("წავშალო ბოლო ჩანაწერი?", "Delete the last record?"),
            L("წაშლა", "Delete"),
            L("გაუქმება", "Cancel"));
        if (!confirm)
            return;

        _tracker.RemoveLastRecord(_tracker.CurrentKey, _part);
        Say(L("ბოლო ჩანაწერი წაიშალა.", "Last record deleted."));
        Reload();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "MaintenancePartViewModel.RemoveLast");
        }
    }
}
