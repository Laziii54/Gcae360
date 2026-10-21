using System.Globalization;
using System.Text.Json;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// What OBDb's own data says about one definition in one model year. Pure: no I/O.
///
/// The bundled catalog keeps one flag per row, the command-level "dbg". OBDb carries more: a command's "filter" (the model years it
/// applies to), its "dbgfilter" (the model years in which it is still being debugged) and, under tests/test_cases, the replies
/// recorded for each model year. A row that is not flagged "dbg" can still be a debug command in the year of the car in front of the
/// app (61,598 of the catalog's rows are in that position), and a command that applies from a later year says nothing about an
/// older car. This holds those facts, derived once from OBDb (research/tools/evidence-obdb.js) into Resources/Raw/obdb_year_evidence.json.
/// </summary>
/// <param name="Known">The signal is in the evidence and the model year is known. When false, nothing is concluded.</param>
/// <param name="Applicable">OBDb says the command applies to this model year.</param>
/// <param name="UnderTest">OBDb still flags the command as being debugged in this model year.</param>
/// <param name="RecordedReply">OBDb's tests hold a reply recorded for this model year.</param>
public readonly record struct DefinitionYearEvidence(bool Known, bool Applicable, bool UnderTest, bool RecordedReply)
{
    public static DefinitionYearEvidence Unknown => default;
}

public sealed class ObdbYearEvidence
{
    /// <summary>The years the evidence file speaks about; outside them nothing is concluded.</summary>
    public const int FirstYear = 1996;
    public const int LastYear = 2027;

    private readonly Dictionary<string, Dictionary<string, Entry>> _signals;

    private sealed record Entry(YearSet Applicable, YearSet Debug, YearSet Recorded);

    private ObdbYearEvidence(Dictionary<string, Dictionary<string, Entry>> signals) => _signals = signals;

    public static ObdbYearEvidence Empty { get; } = new(new Dictionary<string, Dictionary<string, Entry>>(StringComparer.OrdinalIgnoreCase));

    public int VehicleCount => _signals.Count;

    public int SignalCount => _signals.Values.Sum(vehicle => vehicle.Count);

    /// <summary>
    /// Reads the evidence file: <c>{"signals": {"BMW-3-Series": {"3SERIES_ODO": ["2012-2027", "2026-2027", "2012-2025"]}}}</c>, each signal
    /// as [applicable years, debug years, years with a recorded reply]; "*" is every year (applicable) or every applicable year (debug).
    /// A file that cannot be read gives <see cref="Empty"/>: the app then behaves as it did before the evidence existed.
    /// </summary>
    public static ObdbYearEvidence Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("signals", out var vehicles) || vehicles.ValueKind != JsonValueKind.Object)
                return Empty;

            var signals = new Dictionary<string, Dictionary<string, Entry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var vehicle in vehicles.EnumerateObject())
            {
                var perVehicle = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                foreach (var signal in vehicle.Value.EnumerateObject())
                {
                    if (signal.Value.ValueKind != JsonValueKind.Array || signal.Value.GetArrayLength() != 3)
                        continue;
                    var parts = signal.Value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
                    perVehicle[signal.Name] = new Entry(YearSet.Parse(parts[0]), YearSet.Parse(parts[1]), YearSet.Parse(parts[2]));
                }

                signals[vehicle.Name] = perVehicle;
            }

            return new ObdbYearEvidence(signals);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Empty;
        }
    }

    /// <summary>
    /// The evidence for one definition. <paramref name="make"/> and <paramref name="model"/> are the catalog row's own (the repository
    /// name of OBDb is "make-model"), <paramref name="signalId"/> its parameter id.
    /// </summary>
    public DefinitionYearEvidence For(string? make, string? model, string? signalId, int? modelYear)
    {
        if (modelYear is not { } year || year < FirstYear || year > LastYear ||
            string.IsNullOrWhiteSpace(make) || string.IsNullOrWhiteSpace(signalId))
        {
            return DefinitionYearEvidence.Unknown;
        }

        if (!_signals.TryGetValue($"{make}-{model}", out var vehicle) || !vehicle.TryGetValue(signalId, out var entry))
            return DefinitionYearEvidence.Unknown;

        var applicable = entry.Applicable.Contains(year);
        return new DefinitionYearEvidence(
            Known: true,
            Applicable: applicable,
            UnderTest: applicable && (entry.Debug.All || entry.Debug.Contains(year)),
            RecordedReply: entry.Recorded.Contains(year));
    }

    /// <summary>"*", "" or "2011-2013, 2015".</summary>
    private readonly struct YearSet
    {
        private readonly (int From, int To)[] _ranges;

        private YearSet(bool all, (int From, int To)[] ranges)
        {
            All = all;
            _ranges = ranges;
        }

        public bool All { get; }

        public bool Contains(int year)
        {
            if (All)
                return true;
            foreach (var (from, to) in _ranges)
            {
                if (year >= from && year <= to)
                    return true;
            }

            return false;
        }

        public static YearSet Parse(string text)
        {
            text = text.Trim();
            if (text == "*")
                return new YearSet(true, Array.Empty<(int, int)>());

            var ranges = new List<(int, int)>();
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var dash = part.IndexOf('-');
                if (dash > 0 &&
                    int.TryParse(part[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var from) &&
                    int.TryParse(part[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var to))
                {
                    ranges.Add((from, to));
                }
                else if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var single))
                {
                    ranges.Add((single, single));
                }
            }

            return new YearSet(false, ranges.ToArray());
        }
    }
}
