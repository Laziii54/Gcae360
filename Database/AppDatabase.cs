using SQLite;
using CarScanner.Models;
using CarScanner.Helpers;
using Microsoft.Extensions.Logging;

namespace CarScanner.Database;

public sealed class AppDatabase
{
    private readonly SQLiteAsyncConnection _database;
    private readonly ILogger<AppDatabase> _logger;
    private readonly Task _initialization;

    public AppDatabase(ILogger<AppDatabase> logger)
    {
        _logger = logger;
        _database = new SQLiteAsyncConnection(Constants.DatabasePath);
        _initialization = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _database.CreateTableAsync<ScanResult>();
        await _database.CreateTableAsync<VehicleProfile>();
        await _database.CreateTableAsync<SettingItem>();
        await _database.CreateTableAsync<ProEntitlement>();
        await _database.CreateTableAsync<MileageRecord>();
        await _database.CreateTableAsync<AccelerationRun>();
        await _database.CreateTableAsync<RefuelRecord>();
        await _database.CreateTableAsync<FuelTripRecord>();
        await _database.CreateTableAsync<MaintenanceReminder>();

        await _database.CreateTableAsync<ScanSession>();
        await _database.CreateTableAsync<ScanDtc>();
        await _database.CreateTableAsync<ScanSensor>();
        await _database.CreateTableAsync<ScanReadiness>();
        await _database.CreateTableAsync<ScanEcuIdentifier>();
        await _database.CreateTableAsync<ScanFreezeFrame>();

        _logger.LogInformation(
            "Database ready at {Path}",
            Constants.DatabasePath);
    }

    private Task Ready() => _initialization;

    public async Task<List<ScanResult>> GetScansAsync()
    {
        await Ready();

        return await _database
            .Table<ScanResult>()
            .OrderByDescending(x => x.ScanDate)
            .ToListAsync();
    }

    public async Task<int> SaveScanAsync(ScanResult item)
    {
        await Ready();

        return item.Id != 0
            ? await _database.UpdateAsync(item)
            : await _database.InsertAsync(item);
    }

    public async Task<int> DeleteScanAsync(ScanResult item)
    {
        await Ready();

        return await _database.DeleteAsync(item);
    }

    public async Task<int> ClearHistoryAsync()
    {
        await Ready();

        var deleted = 0;

        await _database.RunInTransactionAsync(connection =>
        {
            // Remove child records first so the operation remains valid if
            // foreign-key constraints are enabled in a future migration.
            deleted += connection.DeleteAll<ScanDtc>();
            deleted += connection.DeleteAll<ScanSensor>();
            deleted += connection.DeleteAll<ScanReadiness>();
            deleted += connection.DeleteAll<ScanEcuIdentifier>();
            deleted += connection.DeleteAll<ScanFreezeFrame>();
            deleted += connection.DeleteAll<ScanSession>();
            deleted += connection.DeleteAll<ScanResult>();
        });

        return deleted;
    }

    public async Task<VehicleProfile?> GetVehicleAsync(string vin)
    {
        await Ready();

        return await _database
            .Table<VehicleProfile>()
            .Where(x => x.VIN == vin)
            .FirstOrDefaultAsync();
    }

    public async Task SaveVehicleAsync(VehicleProfile vehicle)
    {
        await Ready();

        var old = await GetVehicleAsync(vehicle.VIN);

        if (old == null)
        {
            await _database.InsertAsync(vehicle);
        }
        else
        {
            vehicle.Id = old.Id;
            await _database.UpdateAsync(vehicle);
        }
    }

    public async Task<List<VehicleProfile>> GetVehiclesAsync()
    {
        await Ready();

        return await _database
            .Table<VehicleProfile>()
            .OrderByDescending(x => x.LastSeen)
            .ToListAsync();
    }

    public async Task SaveSettingAsync(string key, string value)
    {
        await Ready();

        var item = await _database.FindAsync<SettingItem>(key);

        if (item == null)
        {
            await _database.InsertAsync(
                new SettingItem
                {
                    Key = key,
                    Value = value
                });
        }
        else
        {
            item.Value = value;
            await _database.UpdateAsync(item);
        }
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        await Ready();

        var item = await _database.FindAsync<SettingItem>(key);

        return item?.Value;
    }

    public async Task SaveProAsync(ProEntitlement value)
    {
        await Ready();

        await _database.InsertOrReplaceAsync(value);
    }

    public async Task<ProEntitlement?> GetProAsync()
    {
        await Ready();

        return await _database.FindAsync<ProEntitlement>("local");
    }

    public async Task<int> SaveMileageAsync(MileageRecord value)
    {
        await Ready();

        return value.Id != 0
            ? await _database.UpdateAsync(value)
            : await _database.InsertAsync(value);
    }

    public async Task<List<MileageRecord>> GetMileageAsync(string vin)
    {
        await Ready();

        return await _database
            .Table<MileageRecord>()
            .Where(x => x.VIN == vin)
            .OrderByDescending(x => x.CheckedAt)
            .ToListAsync();
    }

    public async Task<int> SaveMaintenanceReminderAsync(MaintenanceReminder value)
    {
        await Ready();
        return value.Id == 0
            ? await _database.InsertAsync(value)
            : await _database.UpdateAsync(value);
    }

    public async Task<List<MaintenanceReminder>> GetMaintenanceRemindersAsync(string vehicleKey)
    {
        await Ready();
        return await _database.Table<MaintenanceReminder>()
            .Where(item => item.VehicleKey == vehicleKey)
            .OrderBy(item => item.DueDate)
            .ToListAsync();
    }

    public async Task<MaintenanceReminder?> GetMaintenanceReminderByExternalKeyAsync(
        string vehicleKey,
        string externalKey)
    {
        await Ready();
        return await _database.Table<MaintenanceReminder>()
            .Where(item => item.VehicleKey == vehicleKey && item.ExternalKey == externalKey)
            .FirstOrDefaultAsync();
    }

    public async Task<int> SaveAccelerationRunAsync(AccelerationRun value)
    {
        await Ready();
        return value.Id == 0
            ? await _database.InsertAsync(value)
            : await _database.UpdateAsync(value);
    }

    public async Task<int> DeleteAccelerationRunAsync(int id)
    {
        await Ready();
        return await _database.DeleteAsync<AccelerationRun>(id);
    }

    public async Task<int> ClearAccelerationRunsAsync()
    {
        await Ready();
        return await _database.DeleteAllAsync<AccelerationRun>();
    }

    public async Task<AccelerationRun?> GetLatestAccelerationRunAsync()
    {
        await Ready();
        return await _database.Table<AccelerationRun>()
            .OrderByDescending(x => x.FinishedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<AccelerationRun>> GetAccelerationRunsAsync(string? vin = null)
    {
        await Ready();
        var query = _database.Table<AccelerationRun>();
        if (!string.IsNullOrWhiteSpace(vin))
            query = query.Where(x => x.VIN == vin);
        return await query.OrderByDescending(x => x.FinishedAt).ToListAsync();
    }

    public async Task<AccelerationRun?> GetAccelerationRunAsync(int id)
    {
        await Ready();
        return await _database.FindAsync<AccelerationRun>(id);
    }

    public async Task<int> SaveRefuelAsync(RefuelRecord value)
    {
        await Ready();
        return value.Id == 0
            ? await _database.InsertAsync(value)
            : await _database.UpdateAsync(value);
    }

    public async Task<List<RefuelRecord>> GetRefuelsAsync(string? vin = null)
    {
        await Ready();
        var query = _database.Table<RefuelRecord>();
        if (!string.IsNullOrWhiteSpace(vin))
            query = query.Where(item => item.VIN == vin);
        return await query.OrderByDescending(item => item.RefueledAt).ToListAsync();
    }

    /// <summary>
    /// Saves a trip. A trip with the same VIN that started at the same moment (±2 s) is the same trip and is not stored twice
    /// (returns 0): this protects against a double tap on Stop and against recovering a trip that was already saved.
    /// </summary>
    public async Task<int> SaveFuelTripAsync(FuelTripRecord value)
    {
        await Ready();
        if (value.Id != 0)
            return await _database.UpdateAsync(value);

        var from = value.StartedAt.AddSeconds(-2);
        var to = value.StartedAt.AddSeconds(2);
        var existing = await _database.Table<FuelTripRecord>()
            .Where(item => item.VIN == value.VIN && item.StartedAt >= from && item.StartedAt <= to)
            .FirstOrDefaultAsync();
        return existing is not null ? 0 : await _database.InsertAsync(value);
    }

    public async Task<List<FuelTripRecord>> GetFuelTripsAsync(string? vin = null)
    {
        await Ready();
        var query = _database.Table<FuelTripRecord>();
        if (!string.IsNullOrWhiteSpace(vin))
            query = query.Where(item => item.VIN == vin);
        return await query.OrderByDescending(item => item.FinishedAt).ToListAsync();
    }

    public async Task<int> SaveScanSessionAsync(ScanSession item)
    {
        await Ready();

        return item.Id != 0
            ? await _database.UpdateAsync(item)
            : await _database.InsertAsync(item);
    }

    public async Task<int> SaveScanDtcAsync(ScanDtc item)
    {
        await Ready();

        return await _database.InsertAsync(item);
    }

    public async Task<int> SaveScanSensorAsync(ScanSensor item)
    {
        await Ready();

        return await _database.InsertAsync(item);
    }

    public async Task<int> SaveScanReadinessAsync(ScanReadiness item)
    {
        await Ready();

        return await _database.InsertAsync(item);
    }

    public async Task<int> SaveScanEcuIdentifierAsync(
        ScanEcuIdentifier item)
    {
        await Ready();

        return await _database.InsertAsync(item);
    }

    public async Task<int> SaveScanFreezeFrameAsync(
        ScanFreezeFrame item)
    {
        await Ready();

        return await _database.InsertAsync(item);
    }

    public async Task<List<ScanSession>> GetScanSessionsAsync(
        string? vin = null)
    {
        await Ready();

        var query = _database.Table<ScanSession>();

        if (!string.IsNullOrWhiteSpace(vin))
        {
            query = query.Where(x => x.VIN == vin);
        }

        return await query
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync();
    }

    public async Task DeleteAllVehicleDataAsync()
    {
        await Ready();

        await _database.DeleteAllAsync<ScanResult>();
        await _database.DeleteAllAsync<ScanSession>();
        await _database.DeleteAllAsync<ScanDtc>();
        await _database.DeleteAllAsync<ScanSensor>();
        await _database.DeleteAllAsync<ScanReadiness>();
        await _database.DeleteAllAsync<ScanEcuIdentifier>();
        await _database.DeleteAllAsync<ScanFreezeFrame>();
        await _database.DeleteAllAsync<MileageRecord>();
        await _database.DeleteAllAsync<AccelerationRun>();
        await _database.DeleteAllAsync<RefuelRecord>();
        await _database.DeleteAllAsync<FuelTripRecord>();
        await _database.DeleteAllAsync<MaintenanceReminder>();
        await _database.DeleteAllAsync<VehicleProfile>();
    }

    public async Task<string> ExportDataJsonAsync()
    {
        await Ready();

        var payload = new
        {
            Scans = await GetScansAsync(),
            Sessions = await GetScanSessionsAsync(),
            Dtc = await _database
                .Table<ScanDtc>()
                .ToListAsync(),

            Sensors = await _database
                .Table<ScanSensor>()
                .ToListAsync(),

            Readiness = await _database
                .Table<ScanReadiness>()
                .ToListAsync(),

            EcuIdentifiers = await _database
                .Table<ScanEcuIdentifier>()
                .ToListAsync(),

            FreezeFrames = await _database
                .Table<ScanFreezeFrame>()
                .ToListAsync(),

            Vehicles = await GetVehiclesAsync(),
            Mileage = await _database
                .Table<MileageRecord>()
                .ToListAsync(),
            AccelerationRuns = await GetAccelerationRunsAsync(),
            Refuels = await GetRefuelsAsync(),
            FuelTrips = await GetFuelTripsAsync(),
            MaintenanceReminders = await _database
                .Table<MaintenanceReminder>()
                .ToListAsync(),
            Settings = await _database
                .Table<SettingItem>()
                .ToListAsync(),
            Subscription = await GetProAsync(),
            Preferences = new
            {
                UserId = Microsoft.Maui.Storage.Preferences.Default.Get(
                    "user_id",
                    string.Empty),
                DisplayName = Microsoft.Maui.Storage.Preferences.Default.Get(
                    "user_name",
                    string.Empty),
                Language = Microsoft.Maui.Storage.Preferences.Default.Get(
                    "language",
                    "English"),
                Theme = Microsoft.Maui.Storage.Preferences.Default.Get(
                    "theme",
                    "Light"),
                Units = Microsoft.Maui.Storage.Preferences.Default.Get(
                    "units",
                    "Metric"),
                NotificationsEnabled =
                    Microsoft.Maui.Storage.Preferences.Default.Get(
                        "notifications",
                        false),
                AutoReconnect = Microsoft.Maui.Storage.Preferences.Default.Get(
                    "auto_reconnect",
                    true),
                Polling = Microsoft.Maui.Storage.Preferences.Default.Get(
                    "polling",
                    "Balanced")
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(
            payload,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
    }
}
