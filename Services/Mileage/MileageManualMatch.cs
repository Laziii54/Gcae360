using System.Globalization;

namespace CarScanner.Services.Mileage;

/// <summary>One total-odometer reading and how far it is from the number the person typed.</summary>
public sealed record MileageMatch(MileageEvidence Evidence, double DifferenceKm, bool IsClose);

/// <summary>
/// "Enter manually" on the Mileage page. The person types the distance the dashboard shows; the app looks, among everything the car
/// returned, for readings that are close to it. A dashboard and a module rarely show the very same number (rounding, a different
/// moment, miles against kilometres), so a reading counts as close when it is within 0.5 % of the typed value (at least 5 km).
/// Only total-odometer readings take part: trip counters, "since the codes were cleared" and similar distances never do.
/// Pure: no I/O, nothing is sent to the car. The result says which reading is close - it never calls a number the right one.
/// </summary>
public static class MileageManualMatch
{
    public const double RelativeTolerance = 0.005;
    public const double MinimumToleranceKm = 5;
    public const double MaximumTypedKm = 3_000_000;

    public static double ToleranceKm(double typedKm) => Math.Max(MinimumToleranceKm, typedKm * RelativeTolerance);

    /// <summary>
    /// Reads what a person types: "244418", "244 418", "244,418", "244.418" and "244418.5" all work. One separator followed by exactly
    /// three digits is a thousands separator, anything else after a separator is a decimal part. False for empty, zero or absurd values.
    /// </summary>
    public static bool TryParseTyped(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = new string(text.Where(c => char.IsDigit(c) || c is '.' or ',').ToArray());
        if (cleaned.Length == 0 || !cleaned.Any(char.IsDigit))
            return false;

        var lastSeparator = cleaned.LastIndexOfAny(new[] { '.', ',' });
        string normalized;
        if (lastSeparator < 0)
        {
            normalized = cleaned;
        }
        else
        {
            var digitsAfter = cleaned.Length - lastSeparator - 1;
            var separators = cleaned.Count(c => c is '.' or ',');
            var isThousands = digitsAfter == 3 && (separators == 1 || cleaned.Count(c => c == cleaned[lastSeparator]) > 1);
            normalized = isThousands
                ? cleaned.Replace(".", string.Empty).Replace(",", string.Empty)
                : cleaned[..lastSeparator].Replace(".", string.Empty).Replace(",", string.Empty) + "." + cleaned[(lastSeparator + 1)..];
        }

        return double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
               && value > 0 && value <= MaximumTypedKm;
    }

    /// <summary>
    /// Every total-odometer reading, the closest to the typed value first (the same reading from the same module is listed once).
    /// <paramref name="typedInMiles"/> says which unit the typed number is in.
    /// </summary>
    public static IReadOnlyList<MileageMatch> Rank(IEnumerable<MileageEvidence> evidence, double typedValue, bool typedInMiles)
    {
        var typedKm = typedInMiles ? typedValue * MileagePlausibility.KmPerMile : typedValue;
        var tolerance = ToleranceKm(typedKm);

        return evidence
            .Where(e => e.Measurement == MileageMeasurement.TotalOdometer && e.IsDistance && e.ValueKm > 0)
            .GroupBy(e => (e.Ecu, Value: Math.Round(e.ValueKm, 1), e.Method))
            .Select(group => group.First())
            .Select(e =>
            {
                var difference = Math.Abs(e.ValueKm - typedKm);
                return new MileageMatch(e, difference, difference <= tolerance);
            })
            .OrderBy(match => match.DifferenceKm)
            .ToList();
    }
}
