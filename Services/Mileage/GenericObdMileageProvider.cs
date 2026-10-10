using CarScanner.Models;
using CarScanner.Services.OBD;

namespace CarScanner.Services.Mileage;

/// <summary>
/// Standard OBD-II odometer, Mode 01 PID A6 (SAE J1979). The PID is optional: it is requested only when the ECU
/// advertises it in the supported-PID bitmaps, so nothing is sent on a guess. The ECU that answered is not
/// identified by the protocol, so the reading is labelled as coming from the OBD-II interface, not from "the engine".
/// </summary>
public sealed class GenericObdMileageProvider : IActualMileageProvider
{
    private readonly OBDService _obd;
    public string Name => "Generic OBD-II PID 01 A6";
    public GenericObdMileageProvider(OBDService obd) => _obd = obd;

    public async Task<IReadOnlyList<MileageEvidence>> ReadAllAsync(
        VehicleInfo vehicle, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_obd.IsInitialized)
                await _obd.InitializeAsync(cancellationToken).ConfigureAwait(false);

            var advertised = _obd.AdvertisedPids;
            if (advertised is null)
            {
                await _obd.DiscoverSupportedPidsAsync(cancellationToken).ConfigureAwait(false);
                advertised = _obd.AdvertisedPids;
            }

            var assessment = MileageSourceCompatibility.Evaluate(
                MileageSourceFamily.StandardOdometerPid,
                new MileageSourceFacts(true, true, _obd.IsInitialized, _obd.IsCanProtocol, advertised, 0, false, 0));

            if (!assessment.ShouldTry)
            {
                MileageTrace.Add($"01 A6: not requested ({assessment.Reason})");
                MileageTrace.Attempt(new MileageAttempt(Name, "01 A6", MileageAttemptOutcome.NotTried, string.Empty, assessment.Status, assessment.Reason));
                return Array.Empty<MileageEvidence>();
            }

            var result = await _obd.ReadDistancePidAsync(StandardMileageDecoder.OdometerPid, cancellationToken).ConfigureAwait(false);
            MileageTrace.Add($"01 A6: {result.Kind} {result.Detail}");
            if (!result.IsValue)
            {
                MileageTrace.Attempt(new MileageAttempt(Name, "01 A6", OutcomeOf(result.Kind), result.Detail, assessment.Status, assessment.Reason));
                return Array.Empty<MileageEvidence>();
            }

            var km = result.Value!.Value;
            var plausible = MileagePlausibility.IsPlausibleOdometerKm(km);
            MileageTrace.Attempt(new MileageAttempt(Name, "01 A6",
                plausible ? MileageAttemptOutcome.Read : MileageAttemptOutcome.InvalidReading,
                plausible ? string.Empty : "Value outside the believable range.", assessment.Status, assessment.Reason));

            return new[]
            {
                new MileageEvidence(Name, "OBD-II", km, 0.55,
                    "Optional Mode 01 PID A6 (odometer, 0.1 km per bit). The responding ECU is not identified.")
                {
                    Measurement = MileageMeasurement.TotalOdometer,
                    Status = plausible ? MileageReadStatus.Ok : MileageReadStatus.Implausible,
                    Unit = "km",
                    RawValue = km,
                    SourceKind = MileageSourceKind.StandardObd,
                    Method = "01 A6"
                }
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            MileageTrace.Add($"01 A6: {ex.GetType().Name}");
            MileageTrace.Attempt(new MileageAttempt(Name, "01 A6", MileageAttemptOutcome.ModuleNotResponding, ex.GetType().Name));
            return Array.Empty<MileageEvidence>();
        }
    }

    internal static MileageAttemptOutcome OutcomeOf(StandardReadKind kind) => kind switch
    {
        StandardReadKind.Value => MileageAttemptOutcome.Read,
        StandardReadKind.NoResponse or StandardReadKind.AdapterError => MileageAttemptOutcome.ModuleNotResponding,
        StandardReadKind.NoData or StandardReadKind.NegativeResponse => MileageAttemptOutcome.ReadingUnavailable,
        _ => MileageAttemptOutcome.InvalidReading
    };
}
