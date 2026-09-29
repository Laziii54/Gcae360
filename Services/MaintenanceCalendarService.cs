using System.Globalization;
using System.Text.RegularExpressions;
using CarScanner.Database;
using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Services;

public sealed class MaintenanceCalendarService
{
    private readonly AppDatabase _db;
    private readonly VehicleInfoService _vehicleInfo;
    private readonly OBDService _obd;
    private readonly AppOilReminderService _oilReminder;
    private readonly CalendarNotificationService _notifications;

    public MaintenanceCalendarService(
        AppDatabase db,
        VehicleInfoService vehicleInfo,
        OBDService obd,
        CalendarNotificationService notifications)
    {
        _db = db;
        _vehicleInfo = vehicleInfo;
        _obd = obd;
        _oilReminder = new AppOilReminderService(obd);
        _notifications = notifications;
    }

    public async Task<IReadOnlyList<MaintenanceReminder>> GetAsync()
    {
        var context = await GetVehicleContextAsync();
        await SyncOilReminderAsync(context);

        var items = await _db.GetMaintenanceRemindersAsync(context.Key);
        var mileage = await GetLatestMileageAsync(context.Vin);
        foreach (var item in items)
        {
            if (!item.IsCompleted && mileage.HasValue &&
                item.DueOdometerKm.HasValue && mileage.Value >= item.DueOdometerKm.Value &&
                item.DueDate.Date > DateTime.Today)
            {
                // A mileage interval that arrives first becomes actionable today.
                item.DueDate = DateTime.Today;
            }
        }

        return items.Where(item => !item.IsCompleted)
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.Title)
            .ToArray();
    }

    public async Task<MaintenanceReminder> AddAsync(
        string title,
        string category,
        DateTime dueDate,
        int intervalMonths,
        int intervalKm)
    {
        var context = await GetVehicleContextAsync();
        var mileage = await GetLatestMileageAsync(context.Vin);
        var item = new MaintenanceReminder
        {
            VehicleKey = context.Key,
            Title = title.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? "სხვა" : category,
            DueDate = dueDate.Date,
            IntervalMonths = Math.Max(0, intervalMonths),
            IntervalKm = Math.Max(0, intervalKm),
            OdometerAtCompletionKm = mileage,
            DueOdometerKm = mileage.HasValue && intervalKm > 0 ? mileage + intervalKm : null
        };
        await _db.SaveMaintenanceReminderAsync(item);
        await _notifications.ScheduleAsync(item);
        return item;
    }

    public async Task CompleteAsync(MaintenanceReminder item)
    {
        var context = await GetVehicleContextAsync();
        var mileage = await GetLatestMileageAsync(context.Vin);
        item.LastCompletedAtUtc = DateTime.UtcNow;
        item.OdometerAtCompletionKm = mileage;

        if (item.IntervalMonths <= 0 && item.IntervalKm <= 0)
        {
            item.IsCompleted = true;
            await _db.SaveMaintenanceReminderAsync(item);
            _notifications.Cancel(item.Id);
            return;
        }

        item.IsCompleted = false;
        item.DueDate = item.IntervalMonths > 0
            ? DateTime.Today.AddMonths(item.IntervalMonths)
            : DateTime.Today.AddYears(1);
        item.DueOdometerKm = mileage.HasValue && item.IntervalKm > 0
            ? mileage.Value + item.IntervalKm
            : null;
        await _db.SaveMaintenanceReminderAsync(item);
        await _notifications.ScheduleAsync(item);
    }

    public async Task RescheduleAllAsync()
    {
        try
        {
            foreach (var item in await GetAsync())
                await _notifications.ScheduleAsync(item);
        }
        catch { }
    }

    public async Task SyncServiceReadingsAsync(
        VehicleInfo vehicle,
        IReadOnlyList<ServiceMetricReading> readings)
    {
        if (!IsVin(vehicle.VIN) || readings.Count == 0)
            return;

        var key = vehicle.VIN.Trim().ToUpperInvariant();
        var mileage = await GetLatestMileageAsync(key);
        await SyncReadingGroupAsync(
            key,
            "ecu-general-service",
            "დაგეგმილი სერვისი",
            "სერვისი",
            readings,
            ServiceMetricKind.DaysToNextService,
            ServiceMetricKind.DistanceToNextService,
            mileage);
        await SyncReadingGroupAsync(
            key,
            "ecu-technical-inspection",
            "ტექდათვალიერება",
            "ტექდათვალიერება",
            readings,
            ServiceMetricKind.InspectionDays,
            ServiceMetricKind.InspectionDistance,
            mileage);
    }

    private async Task SyncReadingGroupAsync(
        string vehicleKey,
        string externalKey,
        string title,
        string category,
        IReadOnlyList<ServiceMetricReading> readings,
        ServiceMetricKind dayKind,
        ServiceMetricKind distanceKind,
        double? mileage)
    {
        var days = readings.FirstOrDefault(item => item.Kind == dayKind)?.NumericValue;
        var distance = readings.FirstOrDefault(item => item.Kind == distanceKind)?.NumericValue;
        if (!days.HasValue && !distance.HasValue)
            return;

        var item = await _db.GetMaintenanceReminderByExternalKeyAsync(vehicleKey, externalKey)
            ?? new MaintenanceReminder
            {
                VehicleKey = vehicleKey,
                ExternalKey = externalKey,
                Title = title,
                Category = category,
                IsAutomatic = true
            };
        item.DueDate = days.HasValue
            ? DateTime.Today.AddDays(Math.Max(0, Math.Ceiling(days.Value)))
            : item.Id > 0 ? item.DueDate : DateTime.Today.AddYears(1);
        item.DueOdometerKm = mileage.HasValue && distance.HasValue
            ? mileage.Value + Math.Max(0, distance.Value)
            : item.DueOdometerKm;
        await _db.SaveMaintenanceReminderAsync(item);
        await _notifications.ScheduleAsync(item);
    }

    private async Task SyncOilReminderAsync(VehicleCalendarContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Vin))
            return;

        var status = _oilReminder.Peek(context.Vin, context.Make);
        if (!status.IsStarted)
            return;

        const string externalKey = "app-oil-service";
        var item = await _db.GetMaintenanceReminderByExternalKeyAsync(context.Key, externalKey);
        item ??= new MaintenanceReminder
        {
            VehicleKey = context.Key,
            ExternalKey = externalKey,
            Title = "ძრავის ზეთის შეცვლა",
            Category = "ზეთი",
            IsAutomatic = true
        };

        // If the user completed the automatic item from the calendar, keep the
        // newly calculated interval until the car reports a newer oil change.
        if (item.LastCompletedAtUtc.HasValue &&
            item.LastCompletedAtUtc.Value >= status.ChangedLocal.ToUniversalTime())
            return;

        item.DueDate = status.ChangedLocal.Date.AddDays(status.IntervalDays);
        item.IntervalMonths = 12;
        item.IntervalKm = status.IntervalKm;
        var mileage = await GetLatestMileageAsync(context.Vin);
        if (mileage.HasValue && status.KmDriven.HasValue)
        {
            item.OdometerAtCompletionKm = Math.Max(0, mileage.Value - status.KmDriven.Value);
            item.DueOdometerKm = item.OdometerAtCompletionKm + status.IntervalKm;
        }
        await _db.SaveMaintenanceReminderAsync(item);
        await _notifications.ScheduleAsync(item);
    }

    private async Task<VehicleCalendarContext> GetVehicleContextAsync()
    {
        var current = _vehicleInfo.CurrentVehicle;
        if (current is not null && IsVin(current.VIN))
            return new(current.VIN.Trim().ToUpperInvariant(), current.VIN.Trim().ToUpperInvariant(), current.Manufacturer);

        var recent = (await _db.GetVehiclesAsync()).FirstOrDefault(vehicle => IsVin(vehicle.VIN));
        return recent is null
            ? new("default", string.Empty, string.Empty)
            : new(recent.VIN.Trim().ToUpperInvariant(), recent.VIN.Trim().ToUpperInvariant(), recent.Manufacturer);
    }

    private async Task<double?> GetLatestMileageAsync(string vin)
    {
        if (string.IsNullOrWhiteSpace(vin)) return null;
        if (_obd.IsInitialized)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var live = await _obd.ReadStandardOdometerAsync(timeout.Token);
                if (live is > 0 and < 3_000_000)
                    return live;
            }
            catch { }
        }
        var row = (await _db.GetMileageAsync(vin)).FirstOrDefault();
        if (row is null) return null;
        var match = Regex.Match(row.Value ?? string.Empty, @"\d+(?:[\.,]\d+)?");
        return match.Success && double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static bool IsVin(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length >= 11 &&
        !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

    private sealed record VehicleCalendarContext(string Key, string Vin, string Make);
}
