namespace CarScanner.Models;

public enum DiagnosticOperationStatus
{
    Success,
    SuccessNoCodes,
    SuccessWithCodes,
    ClearedSuccessfully,
    ClearedButFaultReturned,
    Unsupported,
    Timeout,
    TransportError,
    ProtocolError,
    VehicleNotConnected,
    PreconditionsFailed,
    SecurityDenied,
    Cancelled
}

public sealed record DiagnosticOperationResult<T>(
    DiagnosticOperationStatus Status,
    T? Data = default,
    string Message = "",
    string RawResponse = "")
{
    public bool IsSuccess => Status is DiagnosticOperationStatus.Success or DiagnosticOperationStatus.SuccessNoCodes or DiagnosticOperationStatus.SuccessWithCodes or DiagnosticOperationStatus.ClearedSuccessfully;
}
