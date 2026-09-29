namespace CarScanner.Models;

public enum OemDtcReadState
{
    Available,
    NotSupported,
    NoResponse,
    InvalidResponse,
    Error
}

[Flags]
public enum UdsDtcStatus : byte
{
    TestFailed = 0x01,
    TestFailedThisOperationCycle = 0x02,
    Pending = 0x04,
    Confirmed = 0x08,
    TestNotCompletedSinceLastClear = 0x10,
    TestFailedSinceLastClear = 0x20,
    TestNotCompletedThisOperationCycle = 0x40,
    WarningIndicatorRequested = 0x80
}

public sealed record OemRawDtcRecord(
    string RawCodeHex,
    byte StatusByte)
{
    public UdsDtcStatus Status => (UdsDtcStatus)StatusByte;
    public bool IsCurrent => Status.HasFlag(UdsDtcStatus.TestFailed);
    public bool IsPending => Status.HasFlag(UdsDtcStatus.Pending);
    public bool IsConfirmed => Status.HasFlag(UdsDtcStatus.Confirmed);
    public bool RequestsWarningIndicator => Status.HasFlag(UdsDtcStatus.WarningIndicatorRequested);

    public string StatusText
    {
        get
        {
            var values = new List<string>();
            if (IsCurrent) values.Add("Current");
            if (IsPending) values.Add("Pending");
            if (IsConfirmed) values.Add("Confirmed");
            if (RequestsWarningIndicator) values.Add("Warning indicator requested");
            if (Status.HasFlag(UdsDtcStatus.TestFailedSinceLastClear)) values.Add("Failed since clear");
            return values.Count == 0 ? $"Status 0x{StatusByte:X2}" : string.Join(" · ", values);
        }
    }
}

public sealed record OemDtcDecodeResult(
    OemDtcReadState State,
    byte StatusAvailabilityMask,
    IReadOnlyList<OemRawDtcRecord> Records,
    string Detail)
{
    public static OemDtcDecodeResult Failure(OemDtcReadState state, string detail) =>
        new(state, 0, Array.Empty<OemRawDtcRecord>(), detail);
}

public sealed record OemDtcRecord(
    string DefinitionId,
    string EcuId,
    string EcuName,
    string RawCodeHex,
    string DisplayCode,
    string Description,
    byte StatusByte,
    string StatusText,
    bool CatalogMatched,
    string Source);

public sealed record OemEcuDtcResult(
    string DefinitionId,
    string EcuId,
    string EcuName,
    OemDtcReadState State,
    IReadOnlyList<OemDtcRecord> Records,
    string Detail)
{
    public string Status => State switch
    {
        OemDtcReadState.Available => "Available",
        OemDtcReadState.NotSupported => "Not Supported",
        OemDtcReadState.NoResponse => "No Response",
        OemDtcReadState.InvalidResponse => "Invalid Response",
        _ => "Error"
    };
}

public sealed record OemDtcScanResult(
    IReadOnlyList<OemEcuDtcResult> Ecus,
    string Status,
    string Detail)
{
    public static OemDtcScanResult Unavailable(string detail) =>
        new(Array.Empty<OemEcuDtcResult>(), "NOT AVAILABLE", detail);
}
