using System.Globalization;
using System.Text.Json;
using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Converts the small addressing vocabulary used by the bundled OBDb-derived
/// read catalog into an explicit physical CAN target. Unsupported forms fail
/// closed. This resolver is used only for 0x19/0x22 reads.
/// </summary>
public static class ObdbDiagnosticAddressResolver
{
    public static bool TryResolve(
        string? header,
        string? extendedAddress,
        string? rawJson,
        out ServiceDiagnosticAddress address,
        out string reason)
    {
        address = new ServiceDiagnosticAddress(0, null, false, null, "UNSUPPORTED", "");
        reason = string.Empty;

        var hdr = NormalizeHex(header);
        if (hdr.Length == 0)
        {
            reason = "Missing ECU header.";
            return false;
        }

        var metadata = ReadMetadata(rawJson);
        var eaxText = NormalizeHex(!string.IsNullOrWhiteSpace(extendedAddress)
            ? extendedAddress
            : metadata.Eax);

        // BMW/ISO-TP extended addressing used by OBDb definitions such as
        // hdr=6F1, eax=19.  OBDb treats eax as the ECU identifier; when no
        // explicit receive id is present, BMW response id is 0x600 + eax.
        if (hdr.Length == 3 && eaxText.Length == 2 &&
            TryHexByte(eaxText, out var eax) &&
            TryHexInt(hdr, out var bmwTx) && bmwTx <= 0x7FF)
        {
            int? rx = TryParseCanId(metadata.Rax, false);
            if (!rx.HasValue)
                rx = 0x600 + eax;

            address = new ServiceDiagnosticAddress(
                bmwTx,
                rx,
                false,
                eax,
                "11-bit CAN + extended address",
                $"TX 0x{bmwTx:X3} · EAX 0x{eax:X2} · RX 0x{rx.Value:X3}");
            return true;
        }

        // Normal 11-bit physical request.  Only the standardized 7E0-7E7
        // range gets an inferred +8 response id. Other 11-bit headers are sent
        // without a guessed receive filter and the positive DID echo identifies
        // the real responder.
        if (hdr.Length == 3 && TryHexInt(hdr, out var tx11) && tx11 <= 0x7FF)
        {
            var explicitRx = TryParseCanId(metadata.Rax, false);
            int? rx = explicitRx;
            if (!rx.HasValue && tx11 is >= 0x7E0 and <= 0x7E7)
                rx = tx11 + 8;

            address = new ServiceDiagnosticAddress(
                tx11,
                rx,
                false,
                null,
                "11-bit CAN",
                rx.HasValue
                    ? $"TX 0x{tx11:X3} · RX 0x{rx.Value:X3}"
                    : $"TX 0x{tx11:X3} · responder selected by positive DID echo");
            return true;
        }

        // OBDb's DAxx compact normal-fixed form.  Use tester address F1 unless
        // the source explicitly provides tst; use priority 18 unless source
        // explicitly provides pri.  This is ISO-TP 29-bit normal-fixed, not a
        // guessed manufacturer-specific write address.
        if (hdr.Length == 4 && hdr.StartsWith("DA", StringComparison.OrdinalIgnoreCase) &&
            TryHexByte(hdr[2..], out var target))
        {
            var tester = TryHexByte(NormalizeHex(metadata.Tst), out var tst) ? tst : (byte)0xF1;
            var priority = TryHexByte(NormalizeHex(metadata.Pri), out var pri) ? pri : (byte)0x18;

            var tx = (priority << 24) | (0xDA << 16) | (target << 8) | tester;
            var rx = (priority << 24) | (0xDA << 16) | (tester << 8) | target;

            address = new ServiceDiagnosticAddress(
                tx,
                rx,
                true,
                null,
                "29-bit ISO-TP normal-fixed",
                $"TX 0x{tx:X8} · RX 0x{rx:X8}");
            return true;
        }

        // Fully explicit 29-bit header. An explicit rax is respected if present;
        // otherwise no receive id is invented.
        if (hdr.Length == 8 && TryHexInt(hdr, out var tx29) && tx29 <= 0x1FFFFFFF)
        {
            var rx = TryParseCanId(metadata.Rax, true);
            address = new ServiceDiagnosticAddress(
                tx29,
                rx,
                true,
                null,
                "29-bit CAN",
                rx.HasValue
                    ? $"TX 0x{tx29:X8} · RX 0x{rx.Value:X8}"
                    : $"TX 0x{tx29:X8} · responder selected by positive DID echo");
            return true;
        }

        reason = $"Header '{header}' uses an addressing form that is not safe to infer automatically.";
        return false;
    }

    private static Metadata ReadMetadata(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new Metadata();

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            return new Metadata
            {
                Rax = GetText(root, "rax"),
                Eax = GetText(root, "eax"),
                Tst = GetText(root, "tst"),
                Pri = GetText(root, "pri"),
                Proto = GetText(root, "proto")
            };
        }
        catch
        {
            return new Metadata();
        }
    }

    private static string GetText(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static int? TryParseCanId(string? text, bool extended29Bit)
    {
        var normalized = NormalizeHex(text);
        if (normalized.Length == 0 || !TryHexInt(normalized, out var value))
            return null;
        var max = extended29Bit ? 0x1FFFFFFF : 0x7FF;
        return value is >= 0 && value <= max ? value : null;
    }

    private static bool TryHexByte(string? text, out byte value)
    {
        value = 0;
        var normalized = NormalizeHex(text);
        return normalized.Length is > 0 and <= 2 &&
               byte.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryHexInt(string? text, out int value) =>
        int.TryParse(NormalizeHex(text), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    private static string NormalizeHex(string? value) =>
        new((value ?? string.Empty)
            .Trim()
            .Where(Uri.IsHexDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private sealed class Metadata
    {
        public string Rax { get; init; } = string.Empty;
        public string Eax { get; init; } = string.Empty;
        public string Tst { get; init; } = string.Empty;
        public string Pri { get; init; } = string.Empty;
        public string Proto { get; init; } = string.Empty;
    }
}
