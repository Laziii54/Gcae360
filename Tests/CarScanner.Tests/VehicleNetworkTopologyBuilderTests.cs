using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Tests;

public sealed class VehicleNetworkTopologyBuilderTests
{
    [Fact]
    public void GenericResponse_CreatesOnlyConfirmedGenericEcu()
    {
        var topology = VehicleNetworkTopologyBuilder.Build(
            true,
            "ISO 15765-4 CAN 11/500",
            OemModuleDiscoveryResult.NoDefinition("No verified OEM definition"));

        Assert.Equal(2, topology.Nodes.Count);
        Assert.Single(topology.Nodes, node => node.Kind == VehicleNetworkNodeKind.Ecu);
        Assert.Equal(1, topology.AvailableCount);
        Assert.DoesNotContain("ABS", topology.TreeText);
    }

    [Fact]
    public void ProbedDefinitions_PreserveAvailableLimitedAndNoResponseStates()
    {
        var probes = new[]
        {
            Probe("ecm", "ECM", OemModuleProbeState.Available),
            Probe("abs", "ABS", OemModuleProbeState.RespondedLimited),
            Probe("srs", "SRS", OemModuleProbeState.NoResponse)
        };
        var result = OemModuleDiscoveryResult.Partial(
            new[] { new OemDiscoveredModule("ecm", "ECM", "Engine", "Engine Control", "HS-CAN", "capture", "") },
            probes);

        var topology = VehicleNetworkTopologyBuilder.Build(true, "CAN", result);

        Assert.Equal(2, topology.AvailableCount); // Generic OBD + positive ECM
        Assert.Equal(1, topology.LimitedCount);
        Assert.Equal(1, topology.UnavailableCount);
        Assert.Contains("ECM — AVAILABLE", topology.TreeText);
        Assert.Contains("ABS — RESPONDED / LIMITED", topology.TreeText);
        Assert.Contains("SRS — NO RESPONSE", topology.TreeText);
    }

    [Fact]
    public void NoEcuResponse_DoesNotInventNetworkModules()
    {
        var topology = VehicleNetworkTopologyBuilder.Build(false, null, null);

        Assert.Single(topology.Nodes);
        Assert.Equal(VehicleNetworkNodeKind.DiagnosticConnector, topology.Nodes[0].Kind);
        Assert.Equal(0, topology.AvailableCount);
    }

    private static OemModuleProbeResult Probe(
        string definitionId,
        string name,
        OemModuleProbeState state) =>
        new(definitionId, name, name, $"{name} module", "HS-CAN", state,
            "verified probe result", 25, "reviewed capture");
}
