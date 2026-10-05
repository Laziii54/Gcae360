namespace CarScanner.Api.Services;

/// <summary>
/// Compares the car's ECU speed with the phone's GPS speed during a timed
/// run. A fake adapter, an emulator app or edited data gives a speed curve
/// that GPS does not confirm. GPS lags the ECU by up to ~2 s, so the best
/// time shift is searched first.
/// </summary>
public static class RunGpsCheck
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Missing = "missing";

    /// <summary>
    /// <paramref name="obd"/> and <paramref name="gps"/>: (seconds from the run
    /// start, km/h), both from the phone clock.
    /// </summary>
    public static RunGpsCheckResult Check(IReadOnlyList<(double T, double Kph)> obd, IReadOnlyList<(double T, double Kph)> gps)
    {
        var gpsSorted = gps.Where(p => double.IsFinite(p.T) && double.IsFinite(p.Kph) && p.Kph >= 0)
            .OrderBy(p => p.T).ToArray();
        var obdSorted = obd.Where(p => double.IsFinite(p.T) && double.IsFinite(p.Kph))
            .OrderBy(p => p.T).ToArray();

        if (gpsSorted.Length < 5 || obdSorted.Length < 5)
            return new RunGpsCheckResult(Missing, 0, 0, gpsSorted.Length == 0 ? 0 : gpsSorted.Max(p => p.Kph), 0);

        var obdMax = obdSorted.Max(p => p.Kph);
        var gpsMax = gpsSorted.Max(p => p.Kph);

        double bestMedian = double.MaxValue, bestShift = 0;
        var bestCount = 0;
        for (var shift = -1.0; shift <= 2.5001; shift += 0.1)
        {
            var diffs = new List<double>();
            foreach (var (t, kph) in obdSorted)
            {
                if (kph < 20)
                    continue;
                if (Interpolate(gpsSorted, t + shift) is { } g)
                    diffs.Add(Math.Abs(kph - g));
            }

            if (diffs.Count < 5)
                continue;

            diffs.Sort();
            var median = diffs[diffs.Count / 2];
            if (median < bestMedian)
            {
                bestMedian = median;
                bestShift = shift;
                bestCount = diffs.Count;
            }
        }

        if (bestCount < 5)
            return new RunGpsCheckResult(Missing, 0, 0, gpsMax, 0);

        // ECU speed and GPS normally agree within a few km/h.
        var tolerance = Math.Max(7d, obdMax * 0.08d);
        var ok = bestMedian <= tolerance && gpsMax >= obdMax * 0.8d;
        return new RunGpsCheckResult(ok ? Passed : Failed, Math.Round(bestMedian, 1), Math.Round(bestShift, 1), Math.Round(gpsMax, 1), bestCount);
    }

    private static double? Interpolate((double T, double Kph)[] points, double t)
    {
        if (t < points[0].T || t > points[^1].T)
            return null;

        for (var i = 1; i < points.Length; i++)
        {
            if (points[i].T < t)
                continue;
            var a = points[i - 1];
            var b = points[i];
            // A gap longer than 2 s has no reliable value in between.
            if (b.T - a.T > 2.0)
                return null;
            var span = b.T - a.T;
            return span <= 0 ? b.Kph : a.Kph + (b.Kph - a.Kph) * (t - a.T) / span;
        }

        return null;
    }
}

public sealed record RunGpsCheckResult(string Status, double MedianDiffKph, double ShiftSeconds, double GpsMaxKph, int Comparisons);
