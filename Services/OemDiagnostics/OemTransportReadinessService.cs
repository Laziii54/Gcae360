using CarScanner.Models;
using CarScanner.Services.OBD;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Evaluates the transport that was actually negotiated with the vehicle.
/// It performs no OEM address probing and sends no diagnostic request: an
/// OEM ECU can only be queried later through a verified model definition.
/// </summary>
public sealed class OemTransportReadinessService
{
    private readonly OBDService _obd;

    public OemTransportReadinessService(OBDService obd) => _obd = obd;

    public OemTransportReadiness Evaluate(AdapterDiagnostics adapter)
    {
        if (!adapter.BluetoothConnected || !adapter.AdapterInitialized)
        {
            return new OemTransportReadiness(
                CapabilityState.NotConnected,
                "ჯერ საჭიროა Bluetooth adapter-ის კავშირი და ინიციალიზაცია.",
                "Adapter readiness არ დადასტურდა.");
        }

        if (!adapter.VehicleResponding)
        {
            return new OemTransportReadiness(
                CapabilityState.NotAvailable,
                "Adapter ჩანს, მაგრამ ECU პასუხი არ დადასტურდა.",
                "OEM მოდულების შემოწმება არ დაიწყება ECU პასუხის გარეშე.");
        }

        var protocol = _obd.CurrentProtocol;
        if (IsCan(protocol))
        {
            var capability = adapter.CapabilitySnapshot ?? _obd.LastAdapterCapabilitySnapshot;
            if (capability is null)
            {
                return new OemTransportReadiness(
                    CapabilityState.NotAvailable,
                    "CAN აქტიურია, მაგრამ adapter transport ჯერ სრულად არ დატესტილა.",
                    "ერთხელ გაუშვი OBD Adapter Center → TEST; OEM ECU მოთხოვნა მანამდე არ დაიწყება.");
            }

            var requiredTransportChecks = new[]
            {
                "can.headers",
                "can.auto-format",
                "can.flow-control",
                "can.set-header",
                "can.receive-filter-command",
                "can.receive-filter-verified",
                "can.generic-request"
            };
            var missingCheckId = requiredTransportChecks
                .FirstOrDefault(id => capability.Find(id) is null);
            if (missingCheckId is not null)
            {
                return new OemTransportReadiness(
                    CapabilityState.NotAvailable,
                    "Adapter transport-ის სავალდებულო ტესტი არასრულია.",
                    $"ტესტის შედეგი არ არსებობს: {missingCheckId}.");
            }

            var blockingCheck = requiredTransportChecks
                .Select(id => capability.Find(id)!)
                .FirstOrDefault(check => check.State is
                    AdapterCapabilityCheckState.Failed or
                    AdapterCapabilityCheckState.NotSupported);

            if (blockingCheck is not null)
            {
                return new OemTransportReadiness(
                    CapabilityState.NotAvailable,
                    "Adapter-მა საჭირო CAN / ISO-TP transport შესაძლებლობა ვერ დაადასტურა.",
                    $"{blockingCheck.Name}: {blockingCheck.Evidence}");
            }

            var multiFrame = capability.Find("isotp.multiframe");
            var multiFrameEvidence = multiFrame?.State == AdapterCapabilityCheckState.Passed
                ? "ISO-TP multi-frame პასუხიც რეალურად დადასტურდა."
                : "ISO-TP multi-frame პასუხი ამ ECU-ზე ჯერ ვერ დადასტურდა; მრავალფრეიმიანი OEM კითხვა მხარდაჭერილად არ ჩაითვლება.";

            return new OemTransportReadiness(
                CapabilityState.Conditional,
                $"CAN transport დადასტურებულია ({capability.Addressing}).",
                $"{multiFrameEvidence} OEM წვდომა დაიშვება მხოლოდ კონკრეტული მარკა/მოდელი/წლისთვის დადასტურებული read-only ECU პროფილით.");
        }

        if (IsIsoOrKwp(protocol))
        {
            var capability = adapter.CapabilitySnapshot ?? _obd.LastAdapterCapabilitySnapshot;
            var legacy = capability?.Find("legacy.transport");
            if (legacy?.State != AdapterCapabilityCheckState.Passed)
            {
                return new OemTransportReadiness(
                    CapabilityState.NotAvailable,
                    "ISO/KWP აქტიურია, მაგრამ physical transport ჯერ არ დადასტურებულა.",
                    "ერთხელ გაუშვი OBD Adapter Center → TEST; დაუდასტურებელი K-Line OEM მოთხოვნა არ გაიგზავნება.");
            }

            return new OemTransportReadiness(
                CapabilityState.Conditional,
                "ISO/KWP ტრანსპორტი აქტიურია.",
                "შეიძლება დაიწყოს მხოლოდ ამ K-Line მანქანისთვის დადასტურებული read-only ECU პროფილი.");
        }

        return new OemTransportReadiness(
            CapabilityState.RequiresVerifiedDefinition,
            $"აქტიური პროტოკოლი: {_obd.CurrentProtocolDescription}.",
            "ამ ტრანსპორტისთვის OEM მოდულების უსაფრთხო პროფილი ჯერ არ არის დადასტურებული.");
    }

    private static bool IsCan(ObdProtocol protocol) => protocol is
        ObdProtocol.ISO_15765_4_CAN_11BIT_500K or
        ObdProtocol.ISO_15765_4_CAN_29BIT_500K or
        ObdProtocol.ISO_15765_4_CAN_11BIT_250K or
        ObdProtocol.ISO_15765_4_CAN_29BIT_250K or
        ObdProtocol.SAE_J1939_CAN;

    private static bool IsIsoOrKwp(ObdProtocol protocol) => protocol is
        ObdProtocol.ISO_9141_2 or
        ObdProtocol.ISO_14230_4_KWP_5BAUD or
        ObdProtocol.ISO_14230_4_KWP_FAST;
}

public sealed record OemTransportReadiness(
    CapabilityState State,
    string Summary,
    string Evidence);
