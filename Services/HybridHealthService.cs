using System.Globalization;
using System.Text.RegularExpressions;
using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Services;

/// <summary>
/// Read-only hybrid battery snapshot. No write, reset, routine-control, or
/// actuator command is sent. A score is produced only from values that the
/// connected vehicle actually returns.
/// </summary>
public sealed class HybridHealthService
{
    private readonly OBDService _obd;
    private readonly ModuleLiveDataService _moduleLive;

    public HybridHealthService(OBDService obd, ModuleLiveDataService moduleLive)
    {
        _obd = obd;
        _moduleLive = moduleLive;
    }

    public async Task<HybridHealthResult> ScanAsync(
        VehicleInfo vehicle,
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        var profile = ResolveProfile(vehicle);
        var vehicleName = VehicleName(vehicle);
        if (profile == HybridProfileKind.Unsupported)
        {
            return Empty(profile, vehicleName,
                "მანქანა ელექტროდ ან ჰიბრიდად ვერ დადასტურდა.",
                "თუ ეს ელექტრო/ჰიბრიდია, ჯერ განაახლე Vehicle Info ან აირჩიე სწორი ბრენდი/მოდელი.");
        }

        if (!_obd.IsInitialized)
        {
            return Empty(profile, vehicleName,
                "OBD ადაპტერი დაკავშირებული არ არის.",
                ValidationText(profile, false));
        }

        progress?.Report("ECU-reported დარჩენილი სიცოცხლის შემოწმება…");
        var ecuLife = await TryReadRemainingLifeAsync(token).ConfigureAwait(false);

        progress?.Report("HV Battery/BMS მოდულების უსაფრთხო read-only ძიება…");
        ModuleLiveDataService.DiscoveryResult? discovery = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        linked.CancelAfter(TimeSpan.FromSeconds(22));
        var found = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = _moduleLive.RunAsync(vehicle, result =>
        {
            discovery = result;
            found.TrySetResult(true);
            return Task.CompletedTask;
        }, null, linked.Token);

        try
        {
            await found.Task.WaitAsync(TimeSpan.FromSeconds(16), token).ConfigureAwait(false);
            if (discovery is { Groups.Count: > 0 })
            {
                progress?.Report("უჯრედების ძაბვა და ტემპერატურა იკითხება…");
                await Task.Delay(TimeSpan.FromSeconds(4), token).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
        }
        finally
        {
            linked.Cancel();
            try { await run.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        var signals = discovery?.Groups.SelectMany(group => group.Signals).ToArray()
            ?? Array.Empty<ModuleLiveSignal>();
        var cells = signals
            .Where(IsCellVoltage)
            .Select(ReadSignalNumber)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Where(value => value is > 0.5 and < 20)
            .ToArray();
        var temperatures = signals
            .Where(IsBatteryTemperature)
            .Select(ReadSignalNumber)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Where(value => value is > -50 and < 120)
            .ToArray();

        var minimumCell = cells.Length > 0 ? cells.Min() : (double?)null;
        var maximumCell = cells.Length > 0 ? cells.Max() : (double?)null;
        var delta = cells.Length >= 2 ? maximumCell - minimumCell : null;
        var minimumTemperature = temperatures.Length > 0 ? temperatures.Min() : (double?)null;
        var maximumTemperature = temperatures.Length > 0 ? temperatures.Max() : (double?)null;
        var score = CalculateScore(ecuLife, delta, maximumTemperature);
        var estimatedLife = ecuLife ?? score;
        var realDataReturned = ecuLife.HasValue || cells.Length > 0 || temperatures.Length > 0;

        var evidence = string.Join(" · ", new[]
        {
            cells.Length > 0 ? $"{cells.Length} voltage reading" : string.Empty,
            temperatures.Length > 0 ? $"{temperatures.Length} temperature reading" : string.Empty,
            ecuLife.HasValue ? "PID 015B remaining life" : string.Empty
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new HybridHealthResult(
            profile,
            vehicleName,
            score,
            Rating(score),
            delta,
            minimumCell,
            maximumCell,
            minimumTemperature,
            maximumTemperature,
            estimatedLife,
            ecuLife.HasValue,
            cells.Length,
            temperatures.Length,
            realDataReturned
                ? "რეალური ECU/BMS მონაცემები მიღებულია."
                : discovery?.NoteKa ?? "HV Battery/BMS მონაცემი ვერ მოიძებნა.",
            ValidationText(profile, realDataReturned),
            string.IsNullOrWhiteSpace(evidence) ? "დადასტურებული მნიშვნელობა არ დაბრუნდა" : evidence);
    }

    public static HybridProfileKind ResolveProfile(VehicleInfo vehicle)
    {
        var make = vehicle.Manufacturer ?? string.Empty;
        var model = vehicle.Model ?? string.Empty;
        if (make.Contains("Toyota", StringComparison.OrdinalIgnoreCase) &&
            model.Contains("Prius", StringComparison.OrdinalIgnoreCase))
            return HybridProfileKind.ToyotaPrius;
        if ((make.Contains("Chevrolet", StringComparison.OrdinalIgnoreCase) ||
             make.Contains("Opel", StringComparison.OrdinalIgnoreCase) ||
             make.Contains("Vauxhall", StringComparison.OrdinalIgnoreCase)) &&
            (model.Contains("Volt", StringComparison.OrdinalIgnoreCase) ||
             model.Contains("Ampera", StringComparison.OrdinalIgnoreCase)))
            return HybridProfileKind.ChevroletVolt;
        if (make.Contains("Hyundai", StringComparison.OrdinalIgnoreCase) &&
            model.Contains("Ioniq", StringComparison.OrdinalIgnoreCase))
            return HybridProfileKind.HyundaiIoniq;
        if (make.Contains("Tesla", StringComparison.OrdinalIgnoreCase))
            return HybridProfileKind.Tesla;

        var fuel = vehicle.FuelType ?? string.Empty;
        var combined = $"{fuel} {model}";
        if (IsElectricBrand(make) || ContainsAny(combined,
                "electric", "battery electric", "bev", "ელექტრო", "Leaf", "Ariya",
                "ID.3", "ID.4", "ID.5", "ID.6", "e-tron", "Taycan", "Mach-E",
                "Bolt EV", "Bolt EUV", "Kona Electric", "EV6", "EV9", "Niro EV"))
            return HybridProfileKind.GenericElectric;
        if (ContainsAny(combined,
                "hybrid", "phev", "plug-in", "plug in", "ჰიბრიდ"))
            return HybridProfileKind.GenericHybrid;
        return HybridProfileKind.Unsupported;
    }

    public static int? CalculateScore(double? ecuLife, double? cellDelta, double? maximumTemperatureC)
    {
        if (!ecuLife.HasValue && !cellDelta.HasValue && !maximumTemperatureC.HasValue)
            return null;

        var score = ecuLife.HasValue ? Math.Clamp(ecuLife.Value, 0, 100) : 85d;
        if (cellDelta.HasValue)
        {
            score -= cellDelta.Value switch
            {
                <= 0.02 => 0,
                <= 0.05 => 5 + (cellDelta.Value - 0.02) / 0.03 * 8,
                <= 0.10 => 13 + (cellDelta.Value - 0.05) / 0.05 * 20,
                _ => 40
            };
        }
        if (maximumTemperatureC.HasValue)
        {
            score -= maximumTemperatureC.Value switch
            {
                <= 40 => 0,
                <= 50 => (maximumTemperatureC.Value - 40) * 1.5,
                _ => 25
            };
        }
        return (int)Math.Round(Math.Clamp(score, 0, 100));
    }

    private async Task<double?> TryReadRemainingLifeAsync(CancellationToken token)
    {
        if (!_obd.IsPidSupported(0x5B)) return null;
        try
        {
            var sensor = (await _obd.ReadLiveValuesAsync(new[] { 0x5B }, token)).FirstOrDefault();
            return sensor is not null && double.TryParse(sensor.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && value is >= 0 and <= 100 ? value : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static bool IsCellVoltage(ModuleLiveSignal signal)
    {
        var name = $"{signal.NameEn} {signal.NameKa}";
        var batteryCellGroup = signal.GroupId.StartsWith("Battery.Cells", StringComparison.OrdinalIgnoreCase) ||
                               signal.GroupId.StartsWith("Battery.Modules", StringComparison.OrdinalIgnoreCase);
        return batteryCellGroup &&
               (name.Contains("voltage", StringComparison.OrdinalIgnoreCase) || name.Contains("ძაბვ", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBatteryTemperature(ModuleLiveSignal signal)
    {
        var name = $"{signal.NameEn} {signal.NameKa}";
        return signal.GroupId.StartsWith("Battery", StringComparison.OrdinalIgnoreCase) &&
               (name.Contains("temperature", StringComparison.OrdinalIgnoreCase) || name.Contains("ტემპერატურ", StringComparison.OrdinalIgnoreCase));
    }

    private static double? ReadSignalNumber(ModuleLiveSignal signal)
    {
        var match = Regex.Match(signal.ValueText ?? string.Empty, @"-?\d+(?:[\.,]\d+)?");
        if (!match.Success || !double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;
        if (signal.Unit.Equals("mV", StringComparison.OrdinalIgnoreCase))
            value /= 1000d;
        return value;
    }

    private static string Rating(int? score) => score switch
    {
        null => "ვერ შეფასდა",
        >= 80 => "კარგია",
        >= 60 => "საჭიროებს ყურადღებას",
        _ => "შესამოწმებელია"
    };

    private static string ValidationText(HybridProfileKind profile, bool realDataReturned)
    {
        var name = profile switch
        {
            HybridProfileKind.ToyotaPrius => "Prius · ეტაპი 1",
            HybridProfileKind.ChevroletVolt => "Volt/Ampera · ეტაპი 2",
            HybridProfileKind.HyundaiIoniq => "Ioniq · ეტაპი 3",
            HybridProfileKind.Tesla => "Tesla · უნივერსალური BMS read-only",
            HybridProfileKind.GenericElectric => "ელექტრო ავტომობილი · უნივერსალური BMS read-only",
            HybridProfileKind.GenericHybrid => "ჰიბრიდი/PHEV · უნივერსალური BMS read-only",
            _ => "ამ მოდელის პროფილი"
        };
        var accessNote = profile == HybridProfileKind.Tesla
            ? " Tesla-ზე შეიძლება საჭირო იყოს მოდელის შესაბამისი დიაგნოსტიკური კაბელი/gateway წვდომა."
            : string.Empty;
        return (realDataReturned
            ? $"{name}: ამ მანქანიდან პასუხი მიღებულია; სხვადასხვა წლებსა და კომპლექტაციებზე საბოლოო ვალიდაცია ჯერ საჭიროა."
            : $"{name}: ნამდვილ მანქანაზე ტესტირება და პროფილის საბოლოო ვალიდაცია საჭიროა.") + accessNote;
    }

    private static bool IsElectricBrand(string make) => ContainsAny(make,
        "Tesla", "Rivian", "Lucid", "Polestar", "BYD", "NIO", "Zeekr",
        "VinFast", "XPeng", "Li Auto", "Leapmotor", "Aiways");

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string VehicleName(VehicleInfo vehicle) => string.Join(" ",
        new[] { vehicle.Year, vehicle.Manufacturer, vehicle.Model }
            .Where(value => !string.IsNullOrWhiteSpace(value) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase)));

    private static HybridHealthResult Empty(
        HybridProfileKind profile,
        string vehicleName,
        string status,
        string validation) =>
        new(profile, vehicleName, null, "ვერ შეფასდა", null, null, null, null, null,
            null, false, 0, 0, status, validation, "დადასტურებული მნიშვნელობა არ დაბრუნდა");
}
