namespace CarScanner.Models;

public enum OemLiveValueState
{
    Available,
    NotSupported,
    NoResponse,
    InvalidResponse,
    Error
}

public sealed record OemLiveValue(
    string DefinitionId,
    string EcuId,
    string EcuName,
    int DataIdentifier,
    string FieldKey,
    string Label,
    string Category,
    double Value,
    string Unit,
    OemLiveValueState State,
    string Evidence,
    string Source)
{
    public string IdentifierText => $"DID 0x{DataIdentifier:X4}";
}

public sealed record OemLiveCommandResult(
    string DefinitionId,
    string EcuId,
    string EcuName,
    int DataIdentifier,
    OemLiveValueState State,
    IReadOnlyList<OemLiveValue> Values,
    string Detail);

public sealed record OemLiveDataReadResult(
    IReadOnlyList<OemLiveCommandResult> Commands,
    string Status,
    string Detail,
    DateTimeOffset CapturedAt,
    double DataRate)
{
    public static OemLiveDataReadResult Unavailable(string detail) =>
        new(Array.Empty<OemLiveCommandResult>(), "NOT AVAILABLE", detail, DateTimeOffset.UtcNow, 0);
}
