namespace CarScanner.Services.Mileage;

/// <summary>What a distance-related value actually is. These are never interchangeable.</summary>
public enum MileageMeasurement
{
    /// <summary>Total distance the vehicle (or the reporting module) has accumulated.</summary>
    TotalOdometer,

    /// <summary>OBD-II PID 0x31: distance since diagnostic trouble codes were cleared. NOT the odometer.</summary>
    DistanceSinceDtcClear,

    /// <summary>OBD-II PID 0x21: distance travelled while the MIL has been on. NOT the odometer.</summary>
    DistanceWithMilOn,

    TripDistance,

    /// <summary>Engine run time in hours. Never converted to a distance.</summary>
    EngineOperatingHours,

    /// <summary>OBD-II PID 0x4D / 0x4E: minutes with MIL on / since codes cleared.</summary>
    MinutesSinceDiagnosticEvent,

    ElectricDriveDistance,

    /// <summary>Calculated from other data — an estimate, never a reading.</summary>
    EstimatedDistance,

    OtherDistance,

    /// <summary>The odometer value a module stored when something happened (DPF regeneration, adaptation, warning, TPMS event). A record of the past: a snapshot, never the current distance.</summary>
    OdometerAtEvent,

    /// <summary>A value tied to the module's installation ("Mileage / Odometer with ECU installed"). Not the current odometer and not compared with it as one.</summary>
    ModuleInstallRecord
}

public enum MileageReadStatus
{
    Ok,

    /// <summary>A value came back but is outside any believable range; it is flagged, not used.</summary>
    Implausible,

    /// <summary>The reply was truncated, damaged or not in the documented format.</summary>
    Malformed
}

/// <summary>Where the way of reading comes from. None of these means "tested on this vehicle".</summary>
public enum MileageSourceKind
{
    /// <summary>SAE J1979 standard PID (an optional PID the ECU advertises).</summary>
    StandardObd,

    /// <summary>Public community definition (OBDb catalog, UDS read).</summary>
    PublicDefinition,

    /// <summary>Message the car broadcasts, decoded with a public definition (opendbc / OVMS); nothing is requested.</summary>
    CanBroadcast,

    /// <summary>A place found earlier on this phone by matching a number the user typed; not independently documented.</summary>
    LearnedOnPhone
}

/// <summary>
/// One distance-related reading that the car returned. <see cref="ValueKm"/> is the distance in kilometres for
/// distance measurements; for time-based measurements it is NaN and <see cref="RawValue"/> / <see cref="Unit"/> carry
/// the number exactly as the module reported it.
/// </summary>
public sealed record MileageEvidence(
    string Source,
    string Ecu,
    double ValueKm,
    double Confidence,
    string Details)
{
    public MileageMeasurement Measurement { get; init; } = MileageMeasurement.TotalOdometer;

    public MileageReadStatus Status { get; init; } = MileageReadStatus.Ok;

    /// <summary>Unit the module reported: "km", "mi", "h" or "min".</summary>
    public string Unit { get; init; } = "km";

    /// <summary>The number in <see cref="Unit"/>, before any conversion. Null = same as <see cref="ValueKm"/>.</summary>
    public double? RawValue { get; init; }

    public MileageSourceKind SourceKind { get; init; } = MileageSourceKind.PublicDefinition;

    /// <summary>The exact request / frame used, e.g. "01 A6" or "22 F40D @ 7E0".</summary>
    public string Method { get; init; } = string.Empty;

    public DateTimeOffset RetrievedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>What happened when an <see cref="MileageMeasurement.OdometerAtEvent"/> value was stored.</summary>
    public MileageEventKind EventKind { get; init; } = MileageEventKind.None;

    /// <summary>The public definition's own name for the value, e.g. "Odometer at last DPF regeneration".</summary>
    public string SignalName { get; init; } = string.Empty;

    /// <summary>
    /// True when the public catalog still flags the definition behind this reading as under test (OBDb's "dbg": whether to debug the
    /// signal responses). The number then comes from a definition that nobody has confirmed yet; it is shown, with that said.
    /// </summary>
    public bool DefinitionUnderTest { get; init; }

    /// <summary>The data bytes of the module's reply exactly as received (hex), kept so a value can be re-checked by hand.</summary>
    public string RawHex { get; init; } = string.Empty;

    public bool IsDistance => !double.IsNaN(ValueKm);

    /// <summary>
    /// Values that mean the same thing share a group; only values of the same group are ever compared. "odometer" holds
    /// the current total-distance copies; every other measurement is its own group.
    /// </summary>
    public string ComparabilityGroup => Measurement == MileageMeasurement.TotalOdometer ? "odometer" : Measurement.ToString();

    public double DisplayValue => RawValue ?? ValueKm;
}
