using System.Collections.ObjectModel;
using System.Globalization;
using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

public partial class CalendarViewModel : BaseViewModel
{
    private readonly MaintenanceCalendarService _calendar;
    private readonly LocalizationService _localization;
    private IReadOnlyList<MaintenanceReminder> _all = Array.Empty<MaintenanceReminder>();
    private DateTime _visibleMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selectedDate = DateTime.Today;

    public ObservableCollection<CalendarDayItem> Days { get; } = new();
    public ObservableCollection<CalendarReminderItem> SelectedReminders { get; } = new();
    public IReadOnlyList<string> Categories { get; } =
        new[] { "ზეთი", "დაზღვევა", "ტექდათვალიერება", "ფილტრი", "საბურავები", "სხვა" };

    [ObservableProperty] private string monthTitle = string.Empty;
    [ObservableProperty] private string selectedDateTitle = string.Empty;
    [ObservableProperty] private bool isAddFormVisible;
    [ObservableProperty] private string newTitle = string.Empty;
    [ObservableProperty] private string selectedCategory = "დაზღვევა";
    [ObservableProperty] private DateTime newDueDate = DateTime.Today.AddDays(1);
    [ObservableProperty] private string intervalMonthsText = "12";
    [ObservableProperty] private string intervalKmText = "0";
    [ObservableProperty] private string validationMessage = string.Empty;

    public bool HasSelectedReminders => SelectedReminders.Count > 0;
    public bool HasNoSelectedReminders => !HasSelectedReminders;

    public CalendarViewModel(
        MaintenanceCalendarService calendar,
        LocalizationService localization)
    {
        _calendar = calendar;
        _localization = localization;
    }

    [RelayCommand]
    private async Task Load()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            _all = await _calendar.GetAsync();
            BuildMonth();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void PreviousMonth()
    {
        _visibleMonth = _visibleMonth.AddMonths(-1);
        _selectedDate = _visibleMonth;
        BuildMonth();
    }

    [RelayCommand]
    private void NextMonth()
    {
        _visibleMonth = _visibleMonth.AddMonths(1);
        _selectedDate = _visibleMonth;
        BuildMonth();
    }

    [RelayCommand]
    private void SelectDay(CalendarDayItem? day)
    {
        if (day is null) return;
        _selectedDate = day.Date;
        if (day.Date.Month != _visibleMonth.Month || day.Date.Year != _visibleMonth.Year)
            _visibleMonth = new DateTime(day.Date.Year, day.Date.Month, 1);
        BuildMonth();
    }

    [RelayCommand]
    private void ToggleAddForm()
    {
        IsAddFormVisible = !IsAddFormVisible;
        ValidationMessage = string.Empty;
        if (IsAddFormVisible)
            NewDueDate = _selectedDate < DateTime.Today ? DateTime.Today : _selectedDate;
    }

    [RelayCommand]
    private async Task AddReminder()
    {
        ValidationMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(NewTitle))
        {
            ValidationMessage = L("ჩაწერე შესასრულებელი სამუშაო.", "Enter the task to be completed.");
            return;
        }

        _ = int.TryParse(IntervalMonthsText, out var months);
        _ = int.TryParse(IntervalKmText, out var kilometers);
        await _calendar.AddAsync(NewTitle, SelectedCategory, NewDueDate, months, kilometers);
        NewTitle = string.Empty;
        IsAddFormVisible = false;
        _selectedDate = NewDueDate.Date;
        _visibleMonth = new DateTime(NewDueDate.Year, NewDueDate.Month, 1);
        await Load();
    }

    [RelayCommand]
    private async Task Complete(CalendarReminderItem? row)
    {
        if (row is null) return;
        var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is not null)
        {
            var confirmed = await page.DisplayAlertAsync(
                L("გაკეთდა", "Completed"),
                L("შემდეგი ვადა ავტომატურად გადაითვლება. გავაგრძელოთ?", "The next due date will be calculated automatically. Continue?"),
                L("დიახ", "Yes"),
                L("გაუქმება", "Cancel"));
            if (!confirmed) return;
        }

        await _calendar.CompleteAsync(row.Model);
        await Load();
    }

    private void BuildMonth()
    {
        var culture = _localization.IsEnglish
            ? CultureInfo.GetCultureInfo("en-US")
            : CultureInfo.GetCultureInfo("ka-GE");
        MonthTitle = _visibleMonth.ToString("MMMM yyyy", culture);
        SelectedDateTitle = _selectedDate.ToString("d MMMM, dddd", culture);

        var first = _visibleMonth;
        var mondayOffset = ((int)first.DayOfWeek + 6) % 7;
        var gridStart = first.AddDays(-mondayOffset);
        Days.Clear();
        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            var events = _all.Where(item => item.DueDate.Date == date.Date).ToArray();
            Days.Add(new CalendarDayItem
            {
                Date = date,
                DayNumber = date.Day.ToString(CultureInfo.InvariantCulture),
                IsInCurrentMonth = date.Month == _visibleMonth.Month && date.Year == _visibleMonth.Year,
                HasReminder = events.Length > 0,
                IsSelected = date.Date == _selectedDate.Date,
                DotColor = events.Any(item => item.DueDate.Date <= DateTime.Today) ? "#EF4444" : "#1D5BD6"
            });
        }

        SelectedReminders.Clear();
        foreach (var item in _all.Where(item => item.DueDate.Date == _selectedDate.Date))
            SelectedReminders.Add(ToDisplay(item));
        OnPropertyChanged(nameof(HasSelectedReminders));
        OnPropertyChanged(nameof(HasNoSelectedReminders));
    }

    private CalendarReminderItem ToDisplay(MaintenanceReminder item)
    {
        var details = new List<string>();
        if (item.DueOdometerKm.HasValue)
            details.Add(L($"ვადა: {item.DueOdometerKm.Value:N0} კმ", $"Due: {item.DueOdometerKm.Value:N0} km").Replace(',', ' '));
        if (item.IntervalMonths > 0)
            details.Add(L($"ინტერვალი: {item.IntervalMonths} თვე", $"Interval: {item.IntervalMonths} months"));
        if (item.IntervalKm > 0)
            details.Add(L($"{item.IntervalKm:N0} კმ", $"{item.IntervalKm:N0} km").Replace(',', ' '));

        return new CalendarReminderItem
        {
            Model = item,
            Title = item.Title,
            Category = item.Category,
            Details = string.Join(" · ", details),
            Source = item.IsAutomatic
                ? L("ავტომატურად — სერვისის მონაცემებიდან", "Automatic — from service data")
                : L("ხელით დამატებული", "Added manually"),
            AccentColor = item.DueDate.Date <= DateTime.Today ? "#EF4444" : "#1D5BD6"
        };
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;
}

public sealed class CalendarDayItem
{
    public DateTime Date { get; init; }
    public string DayNumber { get; init; } = string.Empty;
    public bool IsInCurrentMonth { get; init; }
    public bool HasReminder { get; init; }
    public bool IsSelected { get; init; }
    public string DotColor { get; init; } = "#1D5BD6";
    public double Opacity => IsInCurrentMonth ? 1 : 0.32;
    public string BackgroundColor => IsSelected ? "#1D5BD6" : "Transparent";
    public string TextColor => IsSelected ? "#FFFFFF" : "{DynamicResource TextColor}";
}

public sealed class CalendarReminderItem
{
    public required MaintenanceReminder Model { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string AccentColor { get; init; } = "#1D5BD6";
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
}
