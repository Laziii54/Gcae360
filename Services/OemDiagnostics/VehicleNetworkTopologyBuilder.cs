using System.Text;
using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Builds a diagnostic-path topology, not a guessed physical vehicle network.
/// Bus/ECU nodes are emitted only from a generic ECU response or a verified
/// definition that was actually probed.
/// </summary>
public static class VehicleNetworkTopologyBuilder
{
    public static VehicleNetworkTopology Build(
        bool genericEcuResponded,
        string? genericProtocol,
        OemModuleDiscoveryResult? oem)
    {
        var anyVerifiedResponse = oem?.Probes.Any(probe => probe.State is
            OemModuleProbeState.Available or OemModuleProbeState.RespondedLimited) == true;
        var nodes = new List<VehicleNetworkNode>
        {
            new("connector", string.Empty, "OBD Diagnostic Connector",
                "სკანერის diagnostic path", VehicleNetworkNodeKind.DiagnosticConnector,
                genericEcuResponded || anyVerifiedResponse
                    ? VehicleNetworkNodeState.Available
                    : VehicleNetworkNodeState.NoResponse)
        };

        if (genericEcuResponded)
        {
            nodes.Add(new VehicleNetworkNode(
                "generic-obd", "connector", "Generic OBD-II Powertrain ECU",
                Known(genericProtocol), VehicleNetworkNodeKind.Ecu,
                VehicleNetworkNodeState.Available));
        }

        if (oem is not null)
        {
            foreach (var bus in oem.Probes.Select(item => item.CanBus)
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var busId = $"bus:{NormalizeId(bus)}";
                var busProbes = oem.Probes.Where(item =>
                    item.CanBus.Equals(bus, StringComparison.OrdinalIgnoreCase)).ToArray();
                nodes.Add(new VehicleNetworkNode(
                    busId, "connector", bus,
                    "verified definition-ით მითითებული diagnostic bus",
                    VehicleNetworkNodeKind.NetworkBus,
                    AggregateBusState(busProbes)));

                foreach (var probe in busProbes)
                {
                    nodes.Add(new VehicleNetworkNode(
                        $"ecu:{probe.DefinitionId}", busId, probe.Name,
                        $"{probe.EcuName} · {probe.Detail} · {probe.ElapsedMilliseconds} ms",
                        VehicleNetworkNodeKind.Ecu,
                        Map(probe.State)));
                }
            }
        }

        var tree = BuildTree(nodes);
        var ecuNodes = nodes.Where(node => node.Kind == VehicleNetworkNodeKind.Ecu).ToArray();
        return new VehicleNetworkTopology(
            nodes,
            tree,
            ecuNodes.Count(node => node.State == VehicleNetworkNodeState.Available),
            ecuNodes.Count(node => node.State == VehicleNetworkNodeState.Limited),
            ecuNodes.Count(node => node.State is VehicleNetworkNodeState.NoResponse or
                VehicleNetworkNodeState.Unsupported or VehicleNetworkNodeState.Error));
    }

    private static string BuildTree(IReadOnlyList<VehicleNetworkNode> nodes)
    {
        var root = nodes.First(node => node.Kind == VehicleNetworkNodeKind.DiagnosticConnector);
        var builder = new StringBuilder($"{Icon(root.State)} {root.Name}");
        AppendChildren(builder, nodes, root.Id, string.Empty);
        return builder.ToString();
    }

    private static void AppendChildren(
        StringBuilder builder,
        IReadOnlyList<VehicleNetworkNode> nodes,
        string parentId,
        string indent)
    {
        var children = nodes.Where(node => node.ParentId == parentId).ToArray();
        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            var last = index == children.Length - 1;
            builder.Append('\n').Append(indent).Append(last ? "└── " : "├── ")
                .Append(Icon(child.State)).Append(' ').Append(child.Name)
                .Append(" — ").Append(child.StateText);
            AppendChildren(builder, nodes, child.Id, indent + (last ? "    " : "│   "));
        }
    }

    private static VehicleNetworkNodeState Map(OemModuleProbeState state) => state switch
    {
        OemModuleProbeState.Available => VehicleNetworkNodeState.Available,
        OemModuleProbeState.RespondedLimited => VehicleNetworkNodeState.Limited,
        OemModuleProbeState.NoResponse => VehicleNetworkNodeState.NoResponse,
        OemModuleProbeState.TransportUnsupported => VehicleNetworkNodeState.Unsupported,
        _ => VehicleNetworkNodeState.Error
    };

    private static VehicleNetworkNodeState AggregateBusState(IReadOnlyList<OemModuleProbeResult> probes)
    {
        if (probes.Any(probe => probe.State == OemModuleProbeState.Available))
            return VehicleNetworkNodeState.Available;
        if (probes.Any(probe => probe.State == OemModuleProbeState.RespondedLimited))
            return VehicleNetworkNodeState.Limited;
        if (probes.All(probe => probe.State == OemModuleProbeState.NoResponse))
            return VehicleNetworkNodeState.NoResponse;
        if (probes.All(probe => probe.State == OemModuleProbeState.TransportUnsupported))
            return VehicleNetworkNodeState.Unsupported;
        return VehicleNetworkNodeState.Error;
    }

    private static string Icon(VehicleNetworkNodeState state) => state switch
    {
        VehicleNetworkNodeState.Available => "●",
        VehicleNetworkNodeState.Limited => "◐",
        VehicleNetworkNodeState.NoResponse => "○",
        VehicleNetworkNodeState.Unsupported => "○",
        _ => "×"
    };

    private static string Known(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Protocol unavailable" : value;

    private static string NormalizeId(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
