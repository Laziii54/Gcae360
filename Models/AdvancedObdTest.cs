using CarScanner.Services.OBD;

namespace CarScanner.Models;

public sealed record ObdTestItem(string Kind, string Identifier, string RawValue, string Status, string Description);

/// <summary>
/// A raw standard Mode 05 O₂-monitor result. Exact sensor mapping, units and
/// scaling are ECU-specific, so they are intentionally not guessed here.
/// </summary>
public sealed record Mode05MonitorResult(byte TestId, int Actual, int Limit, byte StatusByte)
{
    public bool IsFlagged => (StatusByte & 0x01) != 0;
    public string Identifier => ObdLang.L($"O₂ ტესტი 0x{TestId:X2}", $"O₂ Test ID 0x{TestId:X2}");
    public string ActualText => ObdLang.L($"მნიშვნელობა: {Actual}", $"Actual: {Actual} raw");
    public string LimitText => ObdLang.L($"ზღვარი: {Limit}", $"Limit: {Limit} raw");
    public string StatusText => IsFlagged ? ObdLang.L("ნორმაში არ არის", "FLAGGED") : ObdLang.L("ნორმაშია", "NOT FLAGGED");
    public string StatusColor => IsFlagged ? "#B3361F" : "#1F8A4C";
    public string RawStatus => ObdLang.L($"სტატუსის ბაიტი: 0x{StatusByte:X2}", $"Status byte: 0x{StatusByte:X2}");
    public string DefinitionNote => ObdLang.L(
        "ზუსტი O₂ სენსორი და ერთეული ამ ტესტისთვის მხოლოდ მწარმოებლის დადასტურებული აღწერით განისაზღვრება.",
        "The exact O₂ sensor and unit for this test ID are defined only by a verified manufacturer definition.");
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
        : ObdLang.L($"{Actual} (ნედლი)", $"{Actual} raw");
    public string LimitsText => IsCan
        ? $"{ObdLang.L("მინ", "MIN")} {CarScanner.Services.OBD.Mode06Definitions.Format(Minimum, UnitScalingId!.Value)} · {ObdLang.L("მაქს", "MAX")} {CarScanner.Services.OBD.Mode06Definitions.Format(Maximum, UnitScalingId!.Value)}"
        : $"{ObdLang.L("მინ", "MIN")} {Minimum} · {ObdLang.L("მაქს", "MAX")} {Maximum}";
    public string AuxiliaryText => IsCan
        ? $"{CarScanner.Services.OBD.Mode06Definitions.TestName(TestId)} · TID ${TestId:X2}"
        : ObdLang.L($"დამხმარე: {Auxiliary}", $"Aux: {Auxiliary}");
    public string ResultText => Passed ? ObdLang.L("ნორმაშია", "PASSED") : ObdLang.L("ნორმაში არ არის", "FAILED");
    public string ResultColor => Passed ? "#1F8A4C" : "#B3361F";

    /// <summary>What this test checks and, if failed, what it usually means.</summary>
    public string ExplanationText => IsCan
        ? CarScanner.Services.OBD.Mode06Definitions.Explain(MonitorId!.Value, TestId, Passed)
        : string.Empty;
    public bool HasExplanation => !string.IsNullOrWhiteSpace(ExplanationText);

    public string DefinitionNote => IsCan
        ? (TestId >= 0x80 ? ObdLang.L("მწარმოებლის ტესტი: ზღვრები ECU-მ თავად დააბრუნა.", "Manufacturer test: the ECU returned its own limits.") : string.Empty)
        : ObdLang.L(
            "ზოგადი Mode 06 მნიშვნელობა — ზუსტი სახელი და ერთეული მხოლოდ მწარმოებლის დადასტურებული აღწერით განისაზღვრება.",
            "Generic Mode 06 value — the exact name and unit are defined only by a confirmed OEM test definition.");
}
