using CarScanner.Services.OBD;

namespace CarScanner.Services.Passive;

/// <summary>
/// Listens passively to the car's CAN bus through the adapter and matches the traffic with the public opendbc
/// databases. Read-only by construction: it only calls the session's monitor method; it has no code path that
/// transmits a frame.
/// </summary>
public sealed class PassiveCanService(OBDService obd, IDbcSource dbc)
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(12);

    public async Task<PassiveCanReport> ListenAsync(TimeSpan? duration, CancellationToken token)
    {
        if (!obd.IsInitialized)
            await obd.InitializeAsync(token).ConfigureAwait(false);
        if (!obd.IsCanProtocol)
            throw new NotSupportedException("Passive CAN needs a CAN car (2008+).");

        var ext29 = obd.CurrentProtocol is ObdProtocol.ISO_15765_4_CAN_29BIT_500K or ObdProtocol.ISO_15765_4_CAN_29BIT_250K;
        IReadOnlyList<CanObservation> observations = Array.Empty<CanObservation>();
        var frames = 0;
        var overflow = false;

        await obd.RunDtcModuleSessionAsync(async session =>
        {
            (observations, frames, overflow) = await PassiveCanAnalyzer.CollectAsync(
                session, ext29, duration ?? DefaultDuration, token).ConfigureAwait(false);
        }, token).ConfigureAwait(false);

        return await PassiveCanAnalyzer.AnalyzeAsync(observations, frames, overflow, ext29, dbc, token).ConfigureAwait(false);
    }
}
