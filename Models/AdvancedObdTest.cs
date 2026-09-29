namespace CarScanner.Models;

public sealed record ObdTestItem(string Kind, string Identifier, string RawValue, string Status, string Description);

/// <summary>
/// A raw standard Mode 05 O₂-monitor result. Exact sensor mapping, units and
/// scaling are ECU-specific, so they are intentionally not guessed here.
/// </summary>
public sealed record Mode05MonitorResult(byte TestId, int Actual, int Limit, byte StatusByte)
{
    public bool IsFlagged => (StatusByte & 0x01) != 0;
    public string Identifier => $"O₂ Test ID 0x{TestId:X2}";
    public string ActualText => $"Actual: {Actual} raw";
    public string LimitText => $"Limit: {Limit} raw";
    public string StatusText => IsFlagged ? "FLAGGED" : "NOT FLAGGED";
    public string StatusColor => IsFlagged ? "#EF4444" : "#4ADE80";
    public string RawStatus => $"Status byte: 0x{StatusByte:X2}";
    public string DefinitionNote => "ზუსტი O₂ sensor, ერთეული და მნიშვნელობა ამ raw Test ID-სთვის მხოლოდ დადასტურებული ECU definition-ით განისაზღვრება.";
}

/// <summary>
/// A raw, standards-based Mode 06 result. The byte-level test identifier is
/// intentionally retained because the human-readable monitor name and unit
/// are manufacturer-specific unless a verified OEM definition is installed.
/// </summary>
public sealed record Mode06MonitorResult(
    byte TestId,
    byte ComponentId,
    int Minimum,
    int Maximum,
    int Actual,
    int Auxiliary)
{
    /// <summary>CAN (ISO 15765-4) results carry the monitor ID and the unit/scaling ID.</summary>
    public byte? MonitorId { get; init; }
    public byte? UnitScalingId { get; init; }
    private bool IsCan => MonitorId.HasValue && UnitScalingId.HasValue;

    public bool Passed => Actual >= Minimum && Actual <= Maximum;
    public string Identifier => IsCan
        ? $"{CarScanner.Services.OBD.Mode06Definitions.MonitorName(MonitorId!.Value)} · MID ${MonitorId:X2}"
        : $"TID 0x{TestId:X2} / CID 0x{ComponentId:X2}";
    public string ActualText => IsCan
        ? CarScanner.Services.OBD.Mode06Definitions.Format(Actual, UnitScalingId!.Value)
        : $"{Actual} raw";
    public string LimitsText => IsCan
        ? $"MIN {CarScanner.Services.OBD.Mode06Definitions.Format(Minimum, UnitScalingId!.Value)} · MAX {CarScanner.Services.OBD.Mode06Definitions.Format(Maximum, UnitScalingId!.Value)}"
        : $"MIN {Minimum} · MAX {Maximum}";
    public string AuxiliaryText => IsCan
        ? $"{CarScanner.Services.OBD.Mode06Definitions.TestName(TestId)} · TID ${TestId:X2}"
        : $"Aux: {Auxiliary}";
    public string ResultText => Passed ? "PASSED" : "FAILED";
    public string ResultColor => Passed ? "#1F8A4C" : "#B3361F";
    public string DefinitionNote => IsCan
        ? (TestId >= 0x80 ? "მწარმოებლის ტესტი: ზღვრები ECU-მ თავად დააბრუნა." : string.Empty)
        : "Generic Mode 06 value — ზუსტი სახელი და ერთეული მხოლოდ დადასტურებული OEM test definition-ით განისაზღვრება.";
}
