using System.Collections.ObjectModel;
using System.Globalization;
using CarScanner.Services.Localization;
using CarScanner.Services.Maintenance;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>One line of the first-setup list (odometer and/or month of the last change).</summary>
public partial class MaintenanceSetupRow : ObservableObject
{
    public PartId Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string OriginalKm { get; init; } = string.Empty;
    public string OriginalDate { get; init; } = string.Empty;

    [ObservableProperty] private string kmText = string.Empty;
    [ObservableProperty] private string dateText = string.Empty;
}

/// <summary>First setup: when was each item changed last (optional, any can be skipped).</summary>
public partial class MaintenanceSetupViewModel : BaseViewModel
{
    private static readonly string[] DateFormats = { "MM.yyyy", "M.yyyy", "dd.MM.yyyy", "d.M.yyyy", "MM/yyyy", "yyyy" };

    private readonly MaintenanceTrackerService _tracker;
    private readonly LocalizationService _localization;

    [ObservableProperty] private string messageText = string.Empty;
    [ObservableProperty] private bool hasMessage;

    public ObservableCollection<MaintenanceSetupRow> Rows { get; } = new();

    public MaintenanceSetupViewModel(MaintenanceTrackerService tracker, LocalizationService localization)
    {
        _tracker = tracker;
        _localization = localization;
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    public string HeaderText => L("პირველი დაყენება", "First setup");
    public string IntroText => L(
        "მიუთითე, როდის შეცვალე ბოლოს: გარბენი, თვე და წელი (მაგ. 03.2024), ან ორივე. არ იცი? დატოვე ცარიელი, მერე დაამატებ.",
        "Enter when each item was last changed: odometer, month and year (e.g. 03.2024), or both. Don't know? Leave it empty and add it later.");
    public string KmPlaceholder => MaintenanceText.DistanceUnit;
    public string DatePlaceholder => L("თთ.წწწწ", "MM.YYYY");
    public string SaveText => L("შენახვა", "Save");
    public string NoteText
    {
        get
        {
            var brand = MaintenanceSchedule.BrandGroupName(_tracker.BrandGroup);
            return brand.Length > 0
                ? L($"ინტერვალები {brand}-ის სტანდარტიდანაა. ზუსტი შენი ძრავისთვის: სერვის წიგნაკი.", $"Intervals come from the {brand} standard. Exact ones for your engine: the service book.")
                : L("ინტერვალები ზოგადი სტანდარტიდანაა. ზუსტი შენი ძრავისთვის: სერვის წიგნაკი.", "Intervals are general values. Exact ones for your engine: the service book.");
        }
    }

    public void Load()
    {
        Title = HeaderText;
        var data = _tracker.Load(_tracker.CurrentKey);
        var english = _localization.IsEnglish;

        Rows.Clear();
        foreach (var part in MaintenanceSchedule.PartsFor(_tracker.Powertrain))
        {
            var last = MaintenanceTrackerService.LastRecord(data, part.Id);
            var km = last?.Km is { } value ? MaintenanceText.ToDisplay(value).ToString("0", CultureInfo.InvariantCulture) : string.Empty;
            var date = last?.DateUtc is { } utc ? utc.ToLocalTime().ToString("MM.yyyy", CultureInfo.InvariantCulture) : string.Empty;
            Rows.Add(new MaintenanceSetupRow
            {
                Id = part.Id,
                Name = english ? part.NameEn : part.NameKa,
                OriginalKm = km,
                OriginalDate = date,
                KmText = km,
                DateText = date
            });
        }

        MessageText = string.Empty;
        HasMessage = false;
    }

    private static bool TryParseDate(string text, out DateTime date)
    {
        date = default;
        var value = text.Trim();
        if (!DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;

        // Only month/year known: use the middle of that month.
        date = value.Count(c => c is '.' or '/') == 2 ? parsed.Date.AddHours(12) : parsed.Date.AddDays(14).AddHours(12);
        if (date > DateTime.Now)
            date = DateTime.Now;
        return parsed.Year >= 1990 && parsed <= DateTime.Now;
    }

    [RelayCommand]
    private async Task Save()
    {
        var key = _tracker.CurrentKey;
        var saved = 0;

        foreach (var row in Rows)
        {
            var kmText = row.KmText?.Trim() ?? string.Empty;
            var dateText = row.DateText?.Trim() ?? string.Empty;
            if (kmText == row.OriginalKm && dateText == row.OriginalDate)
                continue;
            if (kmText.Length == 0 && dateText.Length == 0)
                continue;

            double? km = null;
            if (kmText.Length > 0)
            {
                var digits = new string(kmText.Where(char.IsDigit).ToArray());
                if (!double.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value is <= 0 or >= 2_000_000)
                {
                    Say(L($"{row.Name}: გარბენი არასწორია.", $"{row.Name}: the odometer value is not valid."));
                    return;
                }

                km = MaintenanceText.FromDisplay(value);
            }

            DateTime? date = null;
            if (dateText.Length > 0)
            {
                if (!TryParseDate(dateText, out var parsed))
                {
                    Say(L($"{row.Name}: თარიღი ჩაწერე ასე: 03.2024", $"{row.Name}: write the date like 03.2024"));
                    return;
                }

                date = parsed;
            }

            _tracker.AddRecord(key, row.Id, date, km);
            saved++;
        }

        if (saved == 0)
        {
            Say(L("ახალი ჩანაწერი არ არის.", "Nothing new to save."));
            return;
        }

        await Shell.Current.GoToAsync("..");
    }

    private void Say(string text)
    {
        MessageText = text;
        HasMessage = text.Length > 0;
    }
}
