namespace CarScanner.Models;

public enum OemIdentificationValueState
{
    Available,
    NotSupported,
    NoResponse,
    InvalidResponse,
    Error
}

public sealed record OemIdentificationValue(
    string DefinitionId,
    string EcuId,
    string EcuName,
    string FieldKey,
    string Label,
    string Value,
    string Unit,
    OemIdentificationValueState State,
    string Evidence,
    string Source)
{
    public string DisplayValue => State == OemIdentificationValueState.Available
        ? string.IsNullOrWhiteSpace(Unit) ? Value : $"{Value} {Unit}"
        : "—";

    public string StatusText => State switch
    {
        OemIdentificationValueState.Available => "Available",
        OemIdentificationValueState.NotSupported => "Not Supported",
        OemIdentificationValueState.NoResponse => "No Response",
        OemIdentificationValueState.InvalidResponse => "Invalid Response",
        _ => "Error"
    };

    public string StatusColor => State switch
    {
        OemIdentificationValueState.Available => "#16A34A",
        OemIdentificationValueState.NotSupported => "#64748B",
        OemIdentificationValueState.NoResponse => "#64748B",
        OemIdentificationValueState.InvalidResponse => "#D97706",
        _ => "#DC2626"
    };
}

public sealed record OemIdentificationScanResult(
    IReadOnlyList<OemIdentificationValue> Values,
    string Status,
    string Detail)
{
    public static OemIdentificationScanResult Unavailable(string detail) =>
        new(Array.Empty<OemIdentificationValue>(), "NOT AVAILABLE", detail);
}
