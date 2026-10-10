using CarScanner.Models;
using CarScanner.Services.OBD;

namespace CarScanner.Services.Mileage;

/// <summary>
/// Standard OBD-II distance / time counters that people confuse with the odometer: distance with the MIL on (21),
/// distance since trouble codes were cleared (31), and the matching run times (4D, 4E). They are read only when the
/// ECU advertises them and are shown separately — they are never compared with, or presented as, the odometer.
/// </summary>
public sealed class GenericObdDistanceContextProvider : IActualMileageProvider
{
    private readonly OBDService _obd;
    public string Name => "Generic OBD-II distance counters";
    public GenericObdDistanceContextProvider(OBDService obd) => _obd = obd;

    public async Task<IReadOnlyList<MileageEvidence>> ReadAllAsync(
        VehicleInfo vehicle, CancellationToken cancellationToken = default)
    {
        var list = new List<MileageEvidence>();
        try
        {
            if (!_obd.IsInitialized)
                return list;

            var advertised = _obd.AdvertisedPids;
            if (advertised is null)
            {
                await _obd.DiscoverSupportedPidsAsync(cancellationToken).ConfigureAwait(false);
                advertised = _obd.AdvertisedPids;
            }

            foreach (var pid in StandardMileageDecoder.ContextPids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var method = $"01 {pid:X2}";
                var assessment = MileageSourceCompatibility.Evaluate(
                    MileageSourceFamily.StandardContextPid,
                    new MileageSourceFacts(true, true, true, _obd.IsCanProtocol, advertised, 0, false, 0), pid);
                if (!assessment.ShouldTry)
                {
                    MileageTrace.Attempt(new MileageAttempt(Name, method, MileageAttemptOutcome.NotTried, string.Empty, assessment.Status, assessment.Reason));
                    continue;
                }

                StandardPidResult result;
                try
                {
                    result = await _obd.ReadDistancePidAsync(pid, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    MileageTrace.Attempt(new MileageAttempt(Name, method, MileageAttemptOutcome.ModuleNotResponding, ex.GetType().Name, assessment.Status, assessment.Reason));
                    continue;
                }

                MileageTrace.Add($"{method}: {result.Kind} {result.Detail}");
                MileageTrace.Attempt(new MileageAttempt(Name, method, GenericObdMileageProvider.OutcomeOf(result.Kind), result.Detail, assessment.Status, assessment.Reason));
                if (!result.IsValue)
                    continue;

                var isKm = result.Unit == "km";
                list.Add(new MileageEvidence(Name, "OBD-II", isKm ? result.Value!.Value : double.NaN, 0.5,
                    result.Saturated ? result.Detail : $"Standard Mode 01 PID {pid:X2}.")
                {
                    Measurement = result.Measurement,
                    Unit = result.Unit,
                    RawValue = result.Value,
                    SourceKind = MileageSourceKind.StandardObd,
                    Method = method
                });
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            MileageTrace.Add($"distance counters: {ex.GetType().Name}");
        }

        return list;
    }
}
