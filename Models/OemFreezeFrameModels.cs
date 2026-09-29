namespace CarScanner.Models;

public enum OemFreezeFrameReadState
{
    Available,
    NotSupported,
    NoResponse,
    InvalidResponse,
    NotApplicable,
    Error
}

public sealed record OemFreezeFrameValue(
    int DataIdentifier,
    string Key,
    string Label,
    string Value,
    string Unit)
{
    public string DisplayValue => string.IsNullOrWhiteSpace(Unit) ? Value : $"{Value} {Unit}";
}

public sealed record OemFreezeFrameRecord(
    string RawDtcHex,
    byte DtcStatusByte,
    byte SnapshotRecordNumber,
    IReadOnlyList<OemFreezeFrameValue> Values);

public sealed record OemFreezeFrameDecodeResult(
    OemFreezeFrameReadState State,
    IReadOnlyList<OemFreezeFrameRecord> Records,
    string Detail)
{
    public static OemFreezeFrameDecodeResult Failure(OemFreezeFrameReadState state, string detail) =>
        new(state, Array.Empty<OemFreezeFrameRecord>(), detail);
}

public sealed record OemEcuFreezeFrameResult(
    string DefinitionId,
    string EcuId,
    string EcuName,
    OemFreezeFrameReadState State,
    IReadOnlyList<OemFreezeFrameRecord> Records,
    string Detail);

public sealed record OemFreezeFrameScanResult(
    IReadOnlyList<OemEcuFreezeFrameResult> Ecus,
    string Status,
    string Detail)
{
    public static OemFreezeFrameScanResult Unavailable(string detail) =>
        new(Array.Empty<OemEcuFreezeFrameResult>(), "NOT AVAILABLE", detail);
}
