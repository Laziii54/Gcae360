using CarScanner.Database;
using CarScanner.Models;
using CarScanner.Services;

namespace CarScanner.Services.Mileage;

public sealed class ActualMileageService
{
    private readonly IEnumerable<IActualMileageProvider> _providers;
    private readonly AppDatabase _db;
    private readonly OBDService _obd;

    public ActualMileageService(
        IEnumerable<IActualMileageProvider> providers,
        AppDatabase db,
        OBDService obd)
    {
        _providers = providers;
        _db = db;
        _obd = obd;
    }

    public async Task<ActualMileageResult> ScanAsync(
        VehicleInfo vehicle,
        CancellationToken cancellationToken = default)
    {
        var result = new ActualMileageResult
        {
            VIN = vehicle.VIN
        };
        MileageTrace.Reset();
        MileageTrace.Add($"მანქანა: {vehicle.Manufacturer} {vehicle.Model} {vehicle.Year} · VIN {vehicle.VIN}");

        // ============================================================
        // 1. ჯერ ყველა არსებული Mileage Provider შევამოწმოთ
        // ============================================================
        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var evidence = await provider.ReadAllAsync(
                    vehicle,
                    cancellationToken);

                MileageTrace.Add($"[{provider.Name}] მნიშვნელობა: {evidence.Count}");
                if (evidence.Count > 0)
                    result.Evidence.AddRange(evidence);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                MileageTrace.Add($"[{provider.Name}] შეცდომა: {ex.GetType().Name}");
                // ერთი provider-ის ჩავარდნამ მთელი Mileage scan
                // არ უნდა გააჩეროს.
            }
        }

        // ============================================================
        // 2. თუ Provider-ებმა რამე იპოვეს
        // ============================================================
        if (result.Evidence.Count > 0)
        {
            Decide(result);
        }
        else
        {
            // ========================================================
            // 3. FALLBACK:
            // სხვა წყაროებიდან თუ ვერ მოიძებნა,
            // ვცდილობთ მანქანის Standard OBD odometer PID A6-ს.
            // ========================================================
            await TryStandardOdometerFallbackAsync(
                result,
                cancellationToken);
        }

        result.Log = MileageTrace.Snapshot();

        // ============================================================
        // 4. შედეგის შენახვა ბაზაში
        // ============================================================
        await _db.SaveMileageAsync(new MileageRecord
        {
            VIN = vehicle.VIN,

            Source = result.Evidence.Count > 0
                ? string.Join(
                    " + ",
                    result.Evidence
                        .Select(x => x.Source)
                        .Distinct())
                : result.FinalMileageKm.HasValue
                    ? "Standard OBD-II Odometer"
                    : "Mileage Engine",

            Value = result.FinalMileageKm.HasValue
                ? $"{result.FinalMileageKm.Value:0.0} km"
                : "—",

            Status = result.Status,
            Details = result.Details,
            CheckedAt = DateTime.UtcNow
        });

        return result;
    }

    /// <summary>
    /// გამოიყენება მაშინ, როდესაც სპეციალიზებული ECU/OEM mileage
    /// providers ვერ აბრუნებენ გარბენს.
    ///
    /// ცდილობს Standard OBD-II PID A6 odometer-ის წაკითხვას.
    /// </summary>
    private async Task TryStandardOdometerFallbackAsync(
        ActualMileageResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!_obd.IsInitialized)
                await _obd.InitializeAsync(cancellationToken);

            var odometerKm =
                await _obd.ReadStandardOdometerAsync(cancellationToken);
            MileageTrace.Add($"01 A6 (standard odometer): {(odometerKm.HasValue ? $"{odometerKm:0} km" : "მანქანამ არ დააბრუნა")}");

            // აშკარად არარეალური მნიშვნელობა არ მივიღოთ.
            if (odometerKm.HasValue &&
                odometerKm.Value >= 0 &&
                odometerKm.Value <= 3_000_000)
            {
                result.FinalMileageKm = odometerKm.Value;

                // რადგან მხოლოდ ერთი მიმდინარე OBD წყაროა,
                // ეს არ ნიშნავს რომ გარბენის ორიგინალობა დადასტურდა.
                result.Confidence = 0.55;

                result.Status = "მიმდინარე ოდომეტრი";

                result.Details =
                    $"მანქანის მიმდინარე odometer: {odometerKm.Value:0.0} km" +
                    Environment.NewLine +
                    "სხვა დამოუკიდებელი ECU/OEM mileage წყარო ვერ მოიძებნა." +
                    Environment.NewLine +
                    "ეს არის მანქანიდან წაკითხული მიმდინარე მნიშვნელობა და არა გარბენის ისტორიის დადასტურება.";

                result.HasMismatch = false;
                result.MaximumDifferenceKm = null;

                return;
            }

            SetUnavailable(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            SetUnavailable(result);
        }
    }

    private static void SetUnavailable(ActualMileageResult result)
    {
        result.FinalMileageKm = null;
        result.Confidence = 0;

        result.Status = "ოდომეტრი მიუწვდომელია";

        result.Details =
            "Mileage-ის სპეციალიზებულმა წყაროებმა მონაცემი ვერ დააბრუნეს " +
            "და Standard OBD-II odometer PID A6-იც მიუწვდომელია.";

        result.HasMismatch = false;
        result.MaximumDifferenceKm = null;
    }

    private static void Decide(ActualMileageResult result)
    {
        if (result.Evidence.Count == 0)
        {
            result.Status = "ვერ დადასტურდა";
            result.Details =
                "ამ მანქანამ დადასტურებული odometer წყარო არ დააბრუნა.";
            return;
        }

        var ordered = result.Evidence
            .OrderByDescending(x => x.Confidence)
            .ToArray();

        // ============================================================
        // მხოლოდ ერთი ECU/OEM წყარო
        // ============================================================
        if (result.Evidence.Count == 1)
        {
            var evidence = ordered[0];

            result.FinalMileageKm = evidence.ValueKm;
            result.Confidence = evidence.Confidence;

            result.Status = "მხოლოდ ერთი ECU წყარო";

            result.Details =
                $"{evidence.Source} / {evidence.Ecu}: " +
                $"{evidence.ValueKm:0.0} km" +
                Environment.NewLine +
                "ერთი წყარო გარბენის ორიგინალობას ვერ ადასტურებს.";

            return;
        }

        // ============================================================
        // რამდენიმე დამოუკიდებელი წყარო
        // ============================================================
        // A value far above all the others (more than double the median)
        // is treated as a wrong decode and ignored. Lower values are kept:
        // a lower dashboard value is exactly what a rollback looks like.
        var values = result.Evidence.Select(x => x.ValueKm).OrderBy(x => x).ToArray();
        var median = values[values.Length / 2];
        var usable = result.Evidence
            .Where(x => values.Length < 3 || x.ValueKm <= median * 2)
            .ToArray();
        if (usable.Length == 0)
            usable = result.Evidence.ToArray();

        var min = usable.Min(x => x.ValueKm);
        var max = usable.Max(x => x.ValueKm);

        result.MaximumDifferenceKm = max - min;

        result.ConsistencyToleranceKm =
            Math.Clamp(
                max * 0.001d,
                5d,
                250d);

        result.HasMismatch =
            result.MaximumDifferenceKm >
            result.ConsistencyToleranceKm;

        // The original mileage is the highest value that any module still
        // remembers (a rolled-back dashboard shows less, never more).
        result.FinalMileageKm = max;

        var averageConfidence =
            result.Evidence.Average(x => x.Confidence);

        result.Confidence =
            Math.Clamp(
                averageConfidence +
                (result.HasMismatch ? -0.25 : 0.20),
                0,
                1);

        result.Status = result.HasMismatch
            ? "შესაძლო შეუსაბამობა"
            : "წყაროები თანხვედრაშია";

        result.Details = string.Join(
            Environment.NewLine,
            result.Evidence.Select(x =>
                $"{x.Source} / {x.Ecu}: {x.ValueKm:0.0} km"));

        result.Details +=
            Environment.NewLine +
            $"Confidence: {result.Confidence:P0}";

        result.Details +=
            Environment.NewLine +
            $"მაქსიმალური სხვაობა: " +
            $"{result.MaximumDifferenceKm:0.0} km · " +
            $"დაშვებული განსხვავება: " +
            $"{result.ConsistencyToleranceKm:0.0} km";

        if (result.HasMismatch)
        {
            result.Details +=
                Environment.NewLine +
                "განსხვავება შეიძლება მიუთითებდეს მოდულის შეცვლაზე, " +
                "reset-ზე ან odometer-ის შეუსაბამობაზე; " +
                "ეს არ არის საბოლოო დასკვნა.";
        }
    }
}