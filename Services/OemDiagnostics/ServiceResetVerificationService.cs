using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Domain-level verifier for maintenance resets.
///
/// OemOperationService already verifies the ECU protocol response defined by
/// the reviewed operation package.  This service adds a second, independent
/// check: compare real service values read before and after the operation.
///
/// It never fabricates a successful value.  If a vehicle requires an ignition
/// cycle or the relevant value cannot be read, the result says so explicitly.
/// </summary>
public sealed class ServiceResetVerificationService
{
    public ServiceResetVerificationResult Verify(
        OemOperationDefinition? operation,
        IReadOnlyList<ServiceMetricReading> before,
        IReadOnlyList<ServiceMetricReading> after)
    {
        if (operation is null || operation.Type != OemOperationType.Service)
        {
            return new ServiceResetVerificationResult(
                ServiceResetVerificationState.NotAttempted,
                "This operation is not a service reset.");
        }

        var relevantKinds = GetRelevantKinds(operation);
        var verifyDids = operation.Steps.Where(s => s.Role.Equals("VERIFY", StringComparison.OrdinalIgnoreCase))
            .Select(s => OemOperationPackageParser.ParseRequest(s.RequestHex))
            .Where(bytes => bytes.Length == 3 && bytes[0] == 0x22)
            .Select(bytes => (bytes[1] << 8) | bytes[2]).ToHashSet();
        var comparable = FindComparable(before.Where(v => verifyDids.Contains(v.DataIdentifier)).ToArray(),
            after, relevantKinds);

        if (comparable.Count == 0)
        {
            return new ServiceResetVerificationResult(
                ServiceResetVerificationState.ProtocolVerifiedReadbackUnavailable,
                "The reviewed ECU operation completed, but no matching service value was available for an independent post-reset readback.");
        }

        foreach (var pair in comparable)
        {
            if (ConfirmsReset(pair.Before, pair.After))
            {
                return new ServiceResetVerificationResult(
                    ServiceResetVerificationState.ServiceValueVerified,
                    BuildVerifiedDetail(pair.Before, pair.After),
                    pair.Before,
                    pair.After);
            }
        }

        var first = comparable[0];
        return new ServiceResetVerificationResult(
            ServiceResetVerificationState.ProtocolVerifiedValueUnchanged,
            "The ECU operation was positively verified at protocol level, but the readable service value did not change yet. Do not display a fabricated 100% value; some vehicles require an ignition cycle before the new interval is readable.",
            first.Before,
            first.After);
    }

    private static IReadOnlyList<(ServiceMetricReading Before, ServiceMetricReading After)> FindComparable(
        IReadOnlyList<ServiceMetricReading> before,
        IReadOnlyList<ServiceMetricReading> after,
        IReadOnlySet<ServiceMetricKind> relevantKinds)
    {
        var pairs = new List<(ServiceMetricReading, ServiceMetricReading)>();

        foreach (var oldValue in before)
        {
            if (relevantKinds.Count > 0 && !relevantKinds.Contains(oldValue.Kind))
                continue;

            var newValue = after.FirstOrDefault(candidate =>
                candidate.Kind == oldValue.Kind &&
                candidate.Id.Equals(oldValue.Id, StringComparison.OrdinalIgnoreCase) &&
                candidate.DataIdentifier == oldValue.DataIdentifier &&
                candidate.TransmitCanId == oldValue.TransmitCanId &&
                candidate.ReceiveCanId == oldValue.ReceiveCanId &&
                candidate.Extended29Bit == oldValue.Extended29Bit &&
                candidate.ExtendedAddress == oldValue.ExtendedAddress &&
                candidate.Unit == oldValue.Unit);

            if (newValue is not null)
                pairs.Add((oldValue, newValue));
        }

        return pairs;
    }

    private static IReadOnlySet<ServiceMetricKind> GetRelevantKinds(
        OemOperationDefinition operation)
    {
        var text = $"{operation.Group} {operation.Name} {operation.Description}"
            .ToLowerInvariant();

        if (text.Contains("oil"))
        {
            return new HashSet<ServiceMetricKind>
            {
                ServiceMetricKind.OilLifePercent,
                ServiceMetricKind.DistanceSinceOilChange,
                ServiceMetricKind.OilChangeInterval
            };
        }

        if (text.Contains("inspection") ||
            text.Contains("assyst") ||
            text.Contains("service interval") ||
            text.Contains("cbs") ||
            text.Contains("maintenance"))
        {
            return new HashSet<ServiceMetricKind>
            {
                ServiceMetricKind.DistanceToNextService,
                ServiceMetricKind.DaysToNextService,
                ServiceMetricKind.InspectionDistance,
                ServiceMetricKind.InspectionDays,
                ServiceMetricKind.ServiceStatus,
                ServiceMetricKind.OilLifePercent,
                ServiceMetricKind.DistanceSinceOilChange,
                ServiceMetricKind.OilChangeInterval
            };
        }

        // Unknown service family: compare any real service value instead of
        // guessing which one should move.
        return new HashSet<ServiceMetricKind>();
    }

    private static bool ConfirmsReset(
        ServiceMetricReading before,
        ServiceMetricReading after)
    {
        if (!double.IsFinite(before.NumericValue) ||
            !double.IsFinite(after.NumericValue))
        {
            return false;
        }

        var delta = after.NumericValue - before.NumericValue;

        return before.Kind switch
        {
            // Oil-life resets normally return to a high percentage.
            ServiceMetricKind.OilLifePercent =>
                after.NumericValue is >= 99.5 and <= 100,

            // "Since last oil change" is expected to return near zero.
            ServiceMetricKind.DistanceSinceOilChange =>
                after.NumericValue <= 100 && after.NumericValue < before.NumericValue,

            // Remaining distance/days typically move forward after reset.
            ServiceMetricKind.DistanceToNextService or
            ServiceMetricKind.DaysToNextService or
            ServiceMetricKind.InspectionDistance or
            ServiceMetricKind.InspectionDays or
            ServiceMetricKind.OilChangeInterval =>
                delta >= MinimumMeaningfulChange(before),

            // Categorical/status values are considered confirmed only when the
            // decoded numeric state actually changes.
            ServiceMetricKind.ServiceStatus =>
                Math.Abs(delta) >= 0.5,

            _ => false
        };
    }

    private static double MinimumMeaningfulChange(ServiceMetricReading value)
    {
        var unit = (value.Unit ?? string.Empty).Trim().ToLowerInvariant();
        return unit switch
        {
            "percent" or "%" => 0.5,
            "days" or "day" => 1.0,
            "kilometers" or "kilometer" or "km" => 1.0,
            _ => 0.000001
        };
    }

    private static string BuildVerifiedDetail(
        ServiceMetricReading before,
        ServiceMetricReading after)
    {
        return $"ECU readback confirmed the reset: {before.SourceName} changed from {Format(before)} to {Format(after)}.";
    }

    private static string Format(ServiceMetricReading value)
    {
        var unit = (value.Unit ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(unit)
            ? value.NumericValue.ToString("0.##")
            : $"{value.NumericValue:0.##} {unit}";
    }
}
