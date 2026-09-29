namespace CarScanner.VinCheck.Services;

/// <summary>
/// Camera that can read text from what it currently shows
/// (Android: Camera2 preview + ML Kit text recognition, on the phone).
/// </summary>
public interface IVinCameraReader
{
    /// <summary>Text lines seen in the current camera picture.</summary>
    Task<IReadOnlyList<string>> ReadTextLinesAsync(CancellationToken token);

    /// <summary>Flashlight for dark places (door frame, under the windshield).</summary>
    Task SetTorchAsync(bool on);

    bool HasTorch { get; }
}

/// <summary>Hands a scanned VIN from the camera page back to the VIN page.</summary>
public static class VinScanBridge
{
    private static Action<string>? _pending;

    /// <summary>Called by the page that opens the scanner.</summary>
    public static void Request(Action<string> onScanned) => _pending = onScanned;

    public static void Publish(string vin)
    {
        var pending = _pending;
        _pending = null;
        pending?.Invoke(vin);
    }
}

/// <summary>
/// Finds a VIN in recognised text. OCR mixes up O/0, I/1 and Q/0; those
/// letters never appear in a VIN, so they are corrected. A VIN whose check
/// digit (9th character, North American cars) is right is accepted at once;
/// other VINs must be read the same way in two pictures in a row.
/// </summary>
public sealed class VinTextExtractor
{
    private const string Allowed = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";
    private string? _previous;

    public void Reset() => _previous = null;

    /// <summary>A confirmed VIN, or null (keep scanning).</summary>
    public string? Accept(IEnumerable<string> lines)
    {
        var candidates = Candidates(lines).ToList();
        if (candidates.Count == 0)
            return null;

        var checkedVin = candidates.FirstOrDefault(HasValidCheckDigit);
        if (checkedVin is not null)
        {
            _previous = null;
            return checkedVin;
        }

        var best = candidates[0];
        if (best == _previous)
        {
            _previous = null;
            return best;
        }

        _previous = best;
        return null;
    }

    private static IEnumerable<string> Candidates(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in lines)
        {
            foreach (var text in Variants(raw))
            {
                for (var start = 0; start + 17 <= text.Length; start++)
                {
                    var original = text.Substring(start, 17);
                    // OCR mix-ups: only a couple are believable in one VIN.
                    if (original.Count(c => c is 'O' or 'Q' or 'I') > 2)
                        continue;

                    var window = original.Replace('O', '0').Replace('Q', '0').Replace('I', '1');
                    if (!window.All(c => Allowed.Contains(c)))
                        continue;
                    // A VIN has letters and digits; skip long numbers / words.
                    if (window.Count(char.IsDigit) < 4 || !window.Any(char.IsLetter))
                        continue;
                    // The last 4 characters are always digits (serial number).
                    if (!window[13..].All(char.IsDigit))
                        continue;
                    if (seen.Add(window))
                        yield return window;
                }
            }
        }
    }

    /// <summary>
    /// Pieces of a line that can hold a VIN: each word, and the whole line
    /// without spaces when it is VIN-sized (VINs are sometimes printed in groups).
    /// </summary>
    private static IEnumerable<string> Variants(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;

        var upper = raw.ToUpperInvariant();
        // Labels like "VIN:" or "VIN NO." in front of the number.
        foreach (var label in new[] { "VIN NO.", "VIN NO", "VIN:", "VIN#", "VIN" })
            upper = upper.Replace(label, " ");

        var compact = new string(upper.Where(char.IsLetterOrDigit).ToArray());
        if (compact.Length is >= 17 and <= 19)
            yield return compact;

        foreach (var word in upper.Split(new[] { ' ', '\t', ':', '*', '#', '-' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var w = new string(word.Where(char.IsLetterOrDigit).ToArray());
            if (w.Length is >= 17 and <= 19 && w != compact)
                yield return w;
        }
    }

    /// <summary>ISO 3779 / North American check digit (9th character).</summary>
    public static bool HasValidCheckDigit(string vin)
    {
        if (vin.Length != 17)
            return false;

        int[] weights = { 8, 7, 6, 5, 4, 3, 2, 10, 0, 9, 8, 7, 6, 5, 4, 3, 2 };
        var sum = 0;
        for (var i = 0; i < 17; i++)
            sum += Transliterate(vin[i]) * weights[i];

        var remainder = sum % 11;
        var expected = remainder == 10 ? 'X' : (char)('0' + remainder);
        return vin[8] == expected;
    }

    private static int Transliterate(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        'A' or 'J' => 1,
        'B' or 'K' or 'S' => 2,
        'C' or 'L' or 'T' => 3,
        'D' or 'M' or 'U' => 4,
        'E' or 'N' or 'V' => 5,
        'F' or 'W' => 6,
        'G' or 'P' or 'X' => 7,
        'H' or 'Y' => 8,
        'R' or 'Z' => 9,
        _ => 0
    };
}
