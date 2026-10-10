using System.Text.Json;
using CarScanner.Models;
using CarScanner.Services.Mileage;

namespace CarScanner.Services.Maintenance;

public sealed class PartRecord
{
    public DateTime? DateUtc { get; set; }
    public double? Km { get; set; }
}

public sealed class PartData
{
    public List<PartRecord> History { get; set; } = new();
    public int? CustomKm { get; set; }
    public int? CustomMonths { get; set; }
}

public sealed class VehicleMaintenanceData
{
    public Dictionary<string, PartData> Parts { get; set; } = new();
    public double? OdometerKm { get; set; }
    public DateTime? OdometerUtc { get; set; }
    public bool OdometerFromCar { get; set; }
    public bool ImportedAppOil { get; set; }

    // The car's own oil-life / next-service value (where the car gives it).
    public double? CarOilLifePercent { get; set; }
    public double? CarServiceDistanceKm { get; set; }
    public DateTime? CarServiceUtc { get; set; }
    public string? CarServiceDetail { get; set; }
}

/// <summary>One item's records for reports.</summary>
public sealed record PartHistory(PartDefinition Part, PartInterval Interval, PartStatus Status, IReadOnlyList<PartRecord> Records);

/// <summary>Current odometer and where it came from.</summary>
public sealed record OdometerReading(double Km, DateTime Utc, bool FromCar);

/// <summary>
/// Keeps the per-item service history on the phone (per VIN). Nothing is
/// written to the car. The odometer comes from the standard PID 01 A6, or
/// from the brand odometer identifiers of the catalog (cluster, engine ECU …).
/// </summary>
public sealed class MaintenanceTrackerService
{
    private const string KeyPrefix = "maintenance_v1_";
    private const string AppOilKeyPrefix = "app_oil_reminder_v1_";
    private static readonly TimeSpan CarOdometerFreshFor = TimeSpan.FromMinutes(10);

    private readonly OBDService _obd;
    private readonly VehicleInfoService _vehicle;
    private readonly CatalogOdometerProvider? _catalogOdometer;
    private int _catalogReadRunning;

    /// <summary>A car value is trusted this long; older ones fall back to the schedule.</summary>
    private static readonly TimeSpan CarServiceValidFor = TimeSpan.FromDays(30);

    public MaintenanceTrackerService(
        OBDService obd,
        VehicleInfoService vehicle,
        IEnumerable<IActualMileageProvider> mileageProviders)
    {
        _obd = obd;
        _vehicle = vehicle;
        _catalogOdometer = mileageProviders.OfType<CatalogOdometerProvider>().FirstOrDefault();
    }

    public VehicleInfo? CurrentVehicle => _vehicle.CurrentVehicle;

    /// <summary>VIN when known; otherwise one shared slot so the list still works.</summary>
    public static string VehicleKey(VehicleInfo? vehicle)
    {
        var vin = vehicle?.VIN?.Trim();
        return !string.IsNullOrWhiteSpace(vin) && vin.Length >= 11 && !vin.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? vin.ToUpperInvariant()
            : "no-vin";
    }

    public string CurrentKey => VehicleKey(CurrentVehicle);
    public string BrandGroup => MaintenanceSchedule.BrandGroup(CurrentVehicle?.Manufacturer);
    public PowertrainKind Powertrain => MaintenanceSchedule.PowertrainFrom(CurrentVehicle?.FuelType);

    public VehicleMaintenanceData Load(string key) => LoadData(key);

    /// <summary>
    /// Service history of any vehicle (not only the connected one), for the
    /// PDF report. Only items with at least one record are returned.
    /// </summary>
    public static IReadOnlyList<PartHistory> HistoryFor(VehicleInfo vehicle)
    {
        var key = VehicleKey(vehicle);
        if (key == "no-vin")
            return Array.Empty<PartHistory>();

        var data = LoadData(key);
        var group = MaintenanceSchedule.BrandGroup(vehicle.Manufacturer);
        var result = new List<PartHistory>();
        foreach (var part in MaintenanceSchedule.PartsFor(MaintenanceSchedule.PowertrainFrom(vehicle.FuelType)))
        {
            if (!data.Parts.TryGetValue(part.Id.ToString(), out var stored) || stored.History.Count == 0)
                continue;

            var standard = MaintenanceSchedule.DefaultInterval(group, part.Id);
            var interval = new PartInterval(stored.CustomKm ?? standard.Km, stored.CustomMonths ?? standard.Months);
            var records = stored.History
                .OrderByDescending(record => record.DateUtc ?? DateTime.MinValue)
                .ThenByDescending(record => record.Km ?? 0)
                .ToArray();
            var last = records[0];
            var status = MaintenanceSchedule.Calculate(interval, last.DateUtc?.ToLocalTime(), last.Km, data.OdometerKm, DateTime.Now);
            result.Add(new PartHistory(part, interval, status, records));
        }

        return result;
    }

    public static VehicleMaintenanceData LoadData(string key)
    {
        try
        {
            var json = Preferences.Default.Get(KeyPrefix + key, string.Empty);
            var data = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<VehicleMaintenanceData>(json);
            data ??= new VehicleMaintenanceData();
            ImportAppOilReminder(key, data);
            return data;
        }
        catch (JsonException)
        {
            return new VehicleMaintenanceData();
        }
    }

    public void Save(string key, VehicleMaintenanceData data)
    {
        try
        {
            Preferences.Default.Set(KeyPrefix + key, JsonSerializer.Serialize(data));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Maintenance save failed: {ex.Message}");
        }
    }

    public static PartData PartOf(VehicleMaintenanceData data, PartId id)
    {
        if (!data.Parts.TryGetValue(id.ToString(), out var part))
        {
            part = new PartData();
            data.Parts[id.ToString()] = part;
        }

        return part;
    }

    public PartInterval IntervalFor(VehicleMaintenanceData data, PartId id)
    {
        var standard = MaintenanceSchedule.DefaultInterval(BrandGroup, id);
        if (!data.Parts.TryGetValue(id.ToString(), out var part))
            return standard;
        return new PartInterval(part.CustomKm ?? standard.Km, part.CustomMonths ?? standard.Months);
    }

    public static PartRecord? LastRecord(VehicleMaintenanceData data, PartId id) =>
        data.Parts.TryGetValue(id.ToString(), out var part)
            ? part.History
                .OrderByDescending(record => record.DateUtc ?? DateTime.MinValue)
                .ThenByDescending(record => record.Km ?? 0)
                .FirstOrDefault()
            : null;

    public static OdometerReading? Odometer(VehicleMaintenanceData data) =>
        data.OdometerKm is { } km
            ? new OdometerReading(km, data.OdometerUtc ?? DateTime.UtcNow, data.OdometerFromCar)
            : null;

    public PartStatus StatusOf(VehicleMaintenanceData data, PartId id)
    {
        var last = LastRecord(data, id);
        var status = MaintenanceSchedule.Calculate(
            IntervalFor(data, id),
            last?.DateUtc?.ToLocalTime(),
            last?.Km,
            data.OdometerKm,
            DateTime.Now);

        // Oil: the car's own oil-life or next-service value wins over the schedule.
        return id == PartId.Oil && CarServiceIsFresh(data)
            ? MaintenanceSchedule.FromCar(data.CarOilLifePercent, data.CarServiceDistanceKm, status.LastDate, status.LastKm)
            : status;
    }

    public static bool CarServiceIsFresh(VehicleMaintenanceData data) =>
        data.CarServiceUtc is { } read && DateTime.UtcNow - read < CarServiceValidFor &&
        (data.CarOilLifePercent is not null || data.CarServiceDistanceKm is not null);

    /// <summary>
    /// Stores the oil life / distance to service that the Service page read
    /// from the car (ServiceDataReaderService), so the oil row and the
    /// reminders use the car's own value.
    /// </summary>
    public void StoreCarServiceData(string key, double? oilLifePercent, double? serviceDistanceKm, string detail)
    {
        if (oilLifePercent is null && serviceDistanceKm is null)
            return;

        var data = Load(key);
        data.CarOilLifePercent = oilLifePercent is { } oil && oil is >= 0 and <= 100 ? Math.Round(oil) : null;
        data.CarServiceDistanceKm = serviceDistanceKm is { } km && km is >= -50000 and <= 100000 ? Math.Round(km) : null;
        data.CarServiceUtc = DateTime.UtcNow;
        data.CarServiceDetail = detail;
        Save(key, data);
    }

    /// <summary>
    /// After an oil change is recorded the car value is from before the
    /// change; it is dropped until the car is read again.
    /// </summary>
    private static void ClearCarService(VehicleMaintenanceData data)
    {
        data.CarOilLifePercent = null;
        data.CarServiceDistanceKm = null;
        data.CarServiceUtc = null;
        data.CarServiceDetail = null;
    }

    /// <summary>Reads PID 01 A6 when the adapter is connected and stores it.</summary>
    public async Task<OdometerReading?> RefreshOdometerAsync(string key, CancellationToken token)
    {
        var data = Load(key);
        if (_obd.IsInitialized)
        {
            try
            {
                var value = await _obd.ReadStandardOdometerAsync(token).ConfigureAwait(false);
                if (value is > 0 and < 2_000_000)
                {
                    data.OdometerKm = Math.Round(value.Value);
                    data.OdometerUtc = DateTime.UtcNow;
                    data.OdometerFromCar = true;
                    Save(key, data);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Odometer read failed: {ex.Message}");
            }
        }

        return Odometer(data);
    }

    /// <summary>
    /// True when the brand odometer should be read: the car is connected, its
    /// brand is known and no fresh value came from the car in the last minutes.
    /// </summary>
    public bool NeedsCatalogOdometer(string key)
    {
        var vehicle = CurrentVehicle;
        if (_catalogOdometer is null || !_obd.IsInitialized || vehicle is null ||
            string.IsNullOrWhiteSpace(vehicle.Manufacturer) ||
            vehicle.Manufacturer.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return false;

        var data = Load(key);
        return !(data.OdometerFromCar && data.OdometerUtc is { } read && DateTime.UtcNow - read < CarOdometerFreshFor);
    }

    /// <summary>
    /// Reads the odometer from the brand identifiers (slower: several modules
    /// may be asked). Only one read runs at a time.
    /// </summary>
    public async Task<OdometerReading?> ReadCatalogOdometerAsync(string key, CancellationToken token)
    {
        var vehicle = CurrentVehicle;
        if (_catalogOdometer is null || vehicle is null)
            return null;
        if (Interlocked.Exchange(ref _catalogReadRunning, 1) == 1)
            return null;

        try
        {
            var evidence = await _catalogOdometer.ReadFirstAsync(vehicle, token).ConfigureAwait(false);
            if (evidence is null || evidence.Status != CarScanner.Services.Mileage.MileageReadStatus.Ok || evidence.Measurement != CarScanner.Services.Mileage.MileageMeasurement.TotalOdometer || evidence.ValueKm is <= 0 or >= 2_000_000)
                return null;

            SetCarOdometer(key, evidence.ValueKm);
            return Odometer(Load(key));
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Catalog odometer read failed: {ex.Message}");
            return null;
        }
        finally
        {
            Interlocked.Exchange(ref _catalogReadRunning, 0);
        }
    }

    /// <summary>A value read from the car elsewhere (e.g. the Mileage page).</summary>
    public void SetCarOdometer(string key, double km)
    {
        if (km is <= 0 or >= 2_000_000)
            return;

        var data = Load(key);
        data.OdometerKm = Math.Round(km);
        data.OdometerUtc = DateTime.UtcNow;
        data.OdometerFromCar = true;
        Save(key, data);
    }

    public void SetManualOdometer(string key, double km)
    {
        var data = Load(key);
        data.OdometerKm = Math.Round(km);
        data.OdometerUtc = DateTime.UtcNow;
        data.OdometerFromCar = false;
        Save(key, data);
    }

    public void AddRecord(string key, PartId id, DateTime? localDate, double? km)
    {
        var data = Load(key);
        PartOf(data, id).History.Add(new PartRecord
        {
            DateUtc = localDate?.ToUniversalTime(),
            Km = km is > 0 ? Math.Round(km.Value) : null
        });

        if (id == PartId.Oil && (localDate is null || localDate.Value.Date >= DateTime.Today.AddDays(-1)))
            ClearCarService(data);

        // A newer odometer value typed with the record also updates the current one.
        if (km is { } value && (data.OdometerKm is null || value > data.OdometerKm))
        {
            data.OdometerKm = Math.Round(value);
            data.OdometerUtc = DateTime.UtcNow;
            data.OdometerFromCar = false;
        }

        Save(key, data);
    }

    public void RemoveLastRecord(string key, PartId id)
    {
        var data = Load(key);
        var last = LastRecord(data, id);
        if (last is null)
            return;
        PartOf(data, id).History.Remove(last);
        Save(key, data);
    }

    public void SetInterval(string key, PartId id, int? km, int? months)
    {
        var data = Load(key);
        var part = PartOf(data, id);
        var standard = MaintenanceSchedule.DefaultInterval(BrandGroup, id);
        part.CustomKm = km == standard.Km ? null : km;
        part.CustomMonths = months == standard.Months ? null : months;
        Save(key, data);
    }

    /// <summary>
    /// The earlier "Oil changed" card stored its own state; carry it over once
    /// so the oil row keeps the date and odometer the user already entered.
    /// </summary>
    private static void ImportAppOilReminder(string key, VehicleMaintenanceData data)
    {
        if (data.ImportedAppOil)
            return;

        data.ImportedAppOil = true;
        try
        {
            var json = Preferences.Default.Get(AppOilKeyPrefix + key, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            DateTime? changed = root.TryGetProperty("ChangedUtc", out var date) && date.TryGetDateTime(out var parsed)
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : null;
            double? km = root.TryGetProperty("OdometerAtChange", out var odo) && odo.ValueKind == JsonValueKind.Number
                ? odo.GetDouble()
                : null;

            if (changed is null && km is null)
                return;

            var oil = PartOf(data, PartId.Oil);
            if (oil.History.Count == 0)
                oil.History.Add(new PartRecord { DateUtc = changed, Km = km });
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            System.Diagnostics.Debug.WriteLine($"App oil import skipped: {ex.Message}");
        }
    }
}
