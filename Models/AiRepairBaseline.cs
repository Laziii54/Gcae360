namespace CarScanner.Models;

/// <summary>
/// A user-saved, real OBD snapshot used for before/after repair comparison.
/// It deliberately stores no diagnosis or calculated health claim.
/// </summary>
public sealed class AiRepairBaseline
{
    public string Vin { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
    public List<string> DtcCodes { get; set; } = new();
    public bool? MilOn { get; set; }
    public List<AiRepairSensor> Sensors { get; set; } = new();
}

public sealed class AiRepairSensor
{
    public int Pid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}
