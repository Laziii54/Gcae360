using CarScanner.Database;
using CarScanner.Models;
using CarScanner.Services;

namespace CarScanner.Services.Mileage;

/// <summary>
/// Runs every mileage source, then separates the different distance values and compares only the total-odometer
/// readings (see <see cref="MileageAnalyzer"/>). Read-only: this service never writes to the car.
/// </summary>
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
        MileageTrace.Add($"vehicle: {vehicle.Manufacturer} {vehicle.Model} {vehicle.Year}");

        // The adapter is made ready once, up front, so the providers can tell "nothing to ask" from "adapter not ready".
        try
        {
            if (!_obd.IsInitialized)
                await _obd.InitializeAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            MileageTrace.Add($"adapter not ready: {ex.GetType().Name} — {MileageReadDiagnosis.OfException(ex, linkDown: !_obd.IsAdapterConnected)}");
        }

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var evidence = await provider.ReadAllAsync(vehicle, cancellationToken);
                MileageTrace.Add($"[{provider.Name}] values: {evidence.Count}");
                result.Evidence.AddRange(evidence);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                MileageTrace.Add($"[{provider.Name}] error: {ex.GetType().Name} — {MileageReadDiagnosis.OfException(ex, linkDown: !_obd.IsAdapterConnected)}");
                MileageTrace.Attempt(new MileageAttempt(provider.Name, string.Empty, MileageAttemptOutcome.ModuleNotResponding, MileageReadDiagnosis.ErrorDetail(ex, linkDown: !_obd.IsAdapterConnected)));
                // One provider failing must not stop the whole scan.
            }
        }

        result.Attempts.AddRange(MileageTrace.AttemptsSnapshot());
        result.ApplyAnalysis();
        result.Log = MileageTrace.Snapshot();

        await _db.SaveMileageAsync(new MileageRecord
        {
            VIN = vehicle.VIN,
            Source = result.Analysis.Odometer.Count > 0
                ? string.Join(" + ", result.Analysis.Odometer.Select(x => x.Source).Distinct())
                : "Mileage check",
            Value = result.FinalMileageKm.HasValue
                ? $"{result.FinalMileageKm.Value:0.0} km"
                : "—",
            Status = result.Status,
            Details = result.Details,
            CheckedAt = DateTime.UtcNow
        });

        return result;
    }
}
