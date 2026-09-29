namespace CarScanner.Models;

public enum ServiceResetVerificationState
{
    NotAttempted,
    ServiceValueVerified,
    ProtocolVerifiedReadbackUnavailable,
    ProtocolVerifiedValueUnchanged,
    Failed
}

/// <summary>
/// Independent post-reset readback result.  A write is never presented as a
/// value-confirmed reset unless the connected vehicle reports a changed service
/// value after the reviewed OEM operation completes.
/// </summary>
public sealed record ServiceResetVerificationResult(
    ServiceResetVerificationState State,
    string Detail,
    ServiceMetricReading? Before = null,
    ServiceMetricReading? After = null)
{
    public bool IsValueVerified =>
        State == ServiceResetVerificationState.ServiceValueVerified;
}
