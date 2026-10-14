using CarScanner.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using CarScanner.Services.Maintenance;
using CarScanner.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>Service page: every service item tracked one by one (oil, filters, fluids …).</summary>
public partial class ServiceViewModel
{
    private MaintenanceTrackerService? _maintenance;

    private MaintenanceTrackerService? Maintenance =>
        _maintenance ??= Application.Current?.Handler?.MauiContext?.Services.GetService<MaintenanceTrackerService>();

    public ObservableCollection<MaintenancePartRow> MaintenanceParts { get; } = new();

    [ObservableProperty] private string odometerValueText = "—";
    [ObservableProperty] private string odometerSourceText = "";
    [ObservableProperty] private Color odometerSourceColor = MaintenanceText.UnknownColor;
    [ObservableProperty] private string partsTitleText = "";
    [ObservableProperty] private bool showPartsSetup;

    public string OdometerCaptionText => L("მიმდინარე გარბენი", "Current odometer");
    public string PartsSetupText => L("პირველი დაყენება: მიუთითე ბოლო შეცვლები", "First setup: enter the last changes");
    public string PartsNoteText => L(
        "ინტერვალები მწარმოებლის ჩვეულებრივი პირობებისაა. ზუსტი შენი ძრავისთვის: სერვის წიგნაკი. ნაწილზე დაჭერით შეცვლი.",
        "Intervals are the manufacturer's normal-conditions values. Exact ones for your engine: the service book. Tap an item to change it.")
        + (Preferences.Default.Get("notifications", false)
            ? string.Empty
            : L(" ვადის შეხსენებისთვის პარამეტრებში ჩართე შეტყობინებები.", " Turn on notifications in Settings to get due-date reminders."));

    /// <summary>
    /// The car's own oil life (%) or distance to the next service, read by the
    /// service-data reader above, is used for the oil row too.
    /// </summary>
    private void StoreCarServiceValues(IReadOnlyList<CarScanner.Models.ServiceMetricReading> readings)
    {
        var tracker = Maintenance;
        if (tracker is null || readings.Count == 0)
            return;

        var oil = readings.FirstOrDefault(r => r.Kind == CarScanner.Models.ServiceMetricKind.OilLifePercent);
        var distance = readings.FirstOrDefault(r => r.Kind == CarScanner.Models.ServiceMetricKind.DistanceToNextService);
        double? km = distance is null
            ? null
            : distance.Unit.Trim().ToLowerInvariant() is "mi" or "mile" or "miles"
                ? distance.NumericValue * 1.609344
                : distance.NumericValue;

        if (oil is null && km is null)
            return;

        tracker.StoreCarServiceData(
            tracker.CurrentKey,
            oil?.NumericValue,
            km,
            string.Join(" · ", new[] { oil?.SourceName, distance?.SourceName }.Where(s => !string.IsNullOrWhiteSpace(s))));
        ReloadMaintenanceParts();
    }

    /// <summary>Rebuilds the list from the phone storage (no car access).</summary>
    public void ReloadMaintenanceParts()
    {
        var tracker = Maintenance;
        if (tracker is null)
            return;

        var english = _localization.IsEnglish;
        var key = tracker.CurrentKey;
        var data = tracker.Load(key);

        var odometer = MaintenanceTrackerService.Odometer(data);
        OdometerValueText = odometer is null ? L("ჩაწერე", "Enter") : MaintenanceText.Km(odometer.Km);
        OdometerSourceText = odometer is null
            ? L("მანქანამ არ მისცა · დააჭირე", "Not reported · tap")
            : odometer.FromCar
                ? L("მანქანიდან", "from the car")
                : L($"ხელით · {odometer.Utc.ToLocalTime():dd.MM.yyyy}", $"entered · {odometer.Utc.ToLocalTime():dd.MM.yyyy}");
        OdometerSourceColor = odometer?.FromCar == true ? MaintenanceText.OkColor : MaintenanceText.UnknownColor;

        var vehicle = tracker.CurrentVehicle;
        var carName = string.Join(" ", new[] { vehicle?.Manufacturer, vehicle?.Model }
            .Where(text => !string.IsNullOrWhiteSpace(text) && !text.Equals("Unknown", StringComparison.OrdinalIgnoreCase)));
        PartsTitleText = L("ნაწილები და სითხეები", "Parts and fluids") + (carName.Length > 0 ? $" · {carName}" : string.Empty);

        MaintenanceParts.Clear();
        var anyRecord = false;
        foreach (var part in MaintenanceSchedule.PartsFor(tracker.Powertrain))
        {
            var interval = tracker.IntervalFor(data, part.Id);
            var status = tracker.StatusOf(data, part.Id);
            anyRecord |= status.LastDate is not null || status.LastKm is not null;

            MaintenanceParts.Add(new MaintenancePartRow
            {
                Id = part.Id,
                Name = english ? part.NameEn : part.NameKa,
                Icon = part.Icon,
                ValueText = MaintenanceText.ValueText(status, english),
                ValueColor = MaintenanceText.ColorOf(status.State),
                // Bar = share of the interval still left; a full red bar when overdue.
                Fraction = status.State switch { PartState.Unknown => 0, PartState.Overdue => 1, _ => Math.Max(0.03, status.Fraction) },
                SubText = MaintenanceText.SubText(status, interval, english),
                DueText = MaintenanceText.DueText(status, english),
                PillText = MaintenanceText.PillText(status.State, english),
                PillColor = MaintenanceText.ColorOf(status.State),
                PillBackground = MaintenanceText.PillBackground(status.State)
            });
        }

        ShowPartsSetup = !anyRecord;
        OnPropertyChanged(nameof(PartsNoteText));

        // Phone reminders (respects the Notifications switch in Settings).
        var notifications = Application.Current?.Handler?.MauiContext?.Services.GetService<MaintenanceNotificationService>();
        if (notifications is not null)
            _ = notifications.UpdateAsync(tracker, english);
    }

    /// <summary>Reads the odometer from the car (if connected) and rebuilds the list.</summary>
    private async Task RefreshMaintenancePartsAsync(CancellationToken token)
    {
        var tracker = Maintenance;
        if (tracker is null)
            return;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await tracker.RefreshOdometerAsync(tracker.CurrentKey, timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // Odometer read timed out: the stored value is used.
        }

        ReloadMaintenanceParts();

        // Older cars without PID A6: read the odometer from the brand
        // identifiers in the background, so the page is not held up.
        var key = tracker.CurrentKey;
        if (tracker.NeedsCatalogOdometer(key))
        {
            _ = Task.Run(async () =>
            {
                using var slow = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var reading = await tracker.ReadCatalogOdometerAsync(key, slow.Token).ConfigureAwait(false);
                if (reading is not null)
                    MainThread.BeginInvokeOnMainThread(ReloadMaintenanceParts);
            });
        }
    }

    [RelayCommand]
    private async Task OpenMaintenancePart(MaintenancePartRow? row)
    {
        try
        {
        if (row is null)
            return;
        await CarScanner.Services.SafeNavigation.GoToAsync($"{nameof(MaintenancePartPage)}?part={row.Id}");
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ServiceViewModel.OpenMaintenancePart");
        }
    }

    [RelayCommand]
    private async Task OpenMaintenanceSetup() =>
        await CarScanner.Services.SafeNavigation.GoToAsync(nameof(MaintenanceSetupPage));

    [RelayCommand]
    private async Task EditOdometer()
    {
        try
        {
        var tracker = Maintenance;
        if (tracker is null)
            return;

        var text = await Shell.Current.DisplayPromptLocalizedAsync(
            L("მიმდინარე გარბენი", "Current odometer"),
            L($"ჩაწერე დაფაზე ნაჩვენები გარბენი ({MaintenanceText.DistanceUnit}).", $"Enter the odometer shown on the dashboard ({MaintenanceText.DistanceUnit})."),
            L("შენახვა", "Save"),
            L("გაუქმება", "Cancel"),
            "84000",
            maxLength: 7,
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(text))
            return;

        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (double.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shown) && shown is > 0 and < 2_000_000)
        {
            tracker.SetManualOdometer(tracker.CurrentKey, MaintenanceText.FromDisplay(shown));
            ReloadMaintenanceParts();
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ServiceViewModel.EditOdometer");
        }
    }
}
