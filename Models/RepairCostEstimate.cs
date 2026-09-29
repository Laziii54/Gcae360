namespace CarScanner.Models;

public enum RepairUrgency
{
    CanWait,
    RepairSoon,
    StopNow
}

public sealed class RepairCostEstimate
{
    public string Code { get; init; } = string.Empty;
    public string ProblemName { get; init; } = string.Empty;
    public int PartMinGel { get; init; }
    public int PartMaxGel { get; init; }
    public int LaborMinGel { get; init; }
    public int LaborMaxGel { get; init; }
    public RepairUrgency Urgency { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;

    public string PartPriceText => $"სავარაუდოდ {PartMinGel:N0}–{PartMaxGel:N0} ₾".Replace(',', ' ');
    public string LaborPriceText => $"სავარაუდოდ {LaborMinGel:N0}–{LaborMaxGel:N0} ₾".Replace(',', ' ');
    public string TotalPriceText => $"სავარაუდოდ {PartMinGel + LaborMinGel:N0}–{PartMaxGel + LaborMaxGel:N0} ₾".Replace(',', ' ');
    public string UrgencyText => Urgency switch
    {
        RepairUrgency.StopNow => "ახლავე გაჩერდი",
        RepairUrgency.RepairSoon => "მალე გააკეთე",
        _ => "შეიძლება დაელოდოს"
    };
    public string UrgencyColor => Urgency switch
    {
        RepairUrgency.StopNow => "#EF4444",
        RepairUrgency.RepairSoon => "#F59E0B",
        _ => "#22C55E"
    };
}
