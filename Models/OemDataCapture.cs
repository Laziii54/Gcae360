namespace CarScanner.Models;

public sealed class OemDataCapture
{
    public int SchemaVersion { get; set; } = 3;

    public string CaptureKind { get; set; } = "Service reset validation";
    public bool ReadOnly { get; set; } = true;
    public bool VinRedacted { get; set; } = true;

    public DateTimeOffset CapturedAtUtc { get; set; }

    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string Year { get; set; } = "";
    public string Engine { get; set; } = "";
    public string Protocol { get; set; } = "";

    public List<OemDataCaptureModule> Modules { get; set; } = new();
    public List<ServiceResetCaptureValue> ServiceValues { get; set; } = new();
    public List<ServiceResetCaptureTraceEntry> Trace { get; set; } = new();
}

public sealed class ServiceResetCaptureValue
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string SourceName { get; set; } = "";
    public double NumericValue { get; set; }
    public string Unit { get; set; } = "";
    public string Detail { get; set; } = "";
    public string SourceCatalog { get; set; } = "";
    public string EvidenceLevel { get; set; } = "";
    public int TxCanId { get; set; }
    public int? RxCanId { get; set; }
    public int DataIdentifier { get; set; }
}

public sealed class ServiceResetCaptureTraceEntry
{
    public DateTimeOffset TimestampUtc { get; set; }
    public string Context { get; set; } = "";
    public string Request { get; set; } = "";
    public string Response { get; set; } = "";
    public string Result { get; set; } = "";
    public int? TxCanId { get; set; }
    public int? RxCanId { get; set; }
}

public sealed class OemDataCaptureModule
{
    public string DefinitionId { get; set; } = "";
    public string EcuId { get; set; } = "";
    public string Name { get; set; } = "";

    public int TxCanId { get; set; }
    public int RxCanId { get; set; }

    public bool Extended29Bit { get; set; }

    public string CanBus { get; set; } = "";
    public string State { get; set; } = "";

    public long ElapsedMilliseconds { get; set; }

    // ==========================
    // VERIFIED READ-ONLY UDS
    // ==========================

    public string RequestHex { get; set; } = "";

    public string ResponseHex { get; set; } = "";

    public string ResponseServiceHex { get; set; } = "";

    public string ResponsePayloadHex { get; set; } = "";

    public string NegativeResponseCodeHex { get; set; } = "";

    public bool ResponseCaptured { get; set; }

    public string Detail { get; set; } = "";

    public string Source { get; set; } = "";
}
