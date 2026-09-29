using CarScanner.Models;

namespace CarScanner.Services;

public sealed class RepairCostSession
{
    public VehicleInfo? Vehicle { get; private set; }
    public IReadOnlyList<ErrorCode> Codes { get; private set; } = Array.Empty<ErrorCode>();

    public void Set(VehicleInfo? vehicle, IEnumerable<ErrorCode> codes)
    {
        Vehicle = vehicle;
        Codes = codes.ToArray();
    }
}
