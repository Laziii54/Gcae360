namespace CarScanner.Models;

public enum VehicleNetworkNodeKind
{
    DiagnosticConnector,
    NetworkBus,
    Ecu
}

public enum VehicleNetworkNodeState
{
    Available,
    Limited,
    NoResponse,
    Unsupported,
    Error
}

public sealed record VehicleNetworkNode(
    string Id,
    string ParentId,
    string Name,
    string Detail,
    VehicleNetworkNodeKind Kind,
    VehicleNetworkNodeState State)
{
    public string StateText => State switch
    {
        VehicleNetworkNodeState.Available => "AVAILABLE",
        VehicleNetworkNodeState.Limited => "RESPONDED / LIMITED",
        VehicleNetworkNodeState.NoResponse => "NO RESPONSE",
        VehicleNetworkNodeState.Unsupported => "NOT SUPPORTED",
        _ => "ERROR"
    };

    public string StateColor => State switch
    {
        VehicleNetworkNodeState.Available => "#16A34A",
        VehicleNetworkNodeState.Limited => "#D97706",
        VehicleNetworkNodeState.NoResponse => "#64748B",
        VehicleNetworkNodeState.Unsupported => "#64748B",
        _ => "#DC2626"
    };
}

public sealed record VehicleNetworkTopology(
    IReadOnlyList<VehicleNetworkNode> Nodes,
    string TreeText,
    int AvailableCount,
    int LimitedCount,
    int UnavailableCount)
{
    public string Summary =>
        $"პასუხობს: {AvailableCount} · შეზღუდული პასუხი: {LimitedCount} · პასუხი არ არის/შეცდომა: {UnavailableCount}";
}
