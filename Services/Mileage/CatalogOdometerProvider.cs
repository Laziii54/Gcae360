using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarScanner.Models;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Services.UDS;
using SQLite;

namespace CarScanner.Services.Mileage;

/// <summary>
/// Reads the odometer stored in several modules of the car (instrument
/// cluster, engine ECU, transmission, …) using the odometer identifiers of the
/// bundled OBDb catalog (~240 signals, VW/Audi, BMW, Toyota, Hyundai/Kia,
/// Ford, Nissan, Mazda, Honda, Subaru, Opel, GM …).
///
/// Each module keeps its own copy of the distance. When the dashboard was
/// rolled back, other modules usually still show the original value, so the
/// Mileage page compares them. Read-only (0x22 / 0x21); a value is used only
/// after an exact positive reply from the car.
/// </summary>
public sealed class CatalogOdometerProvider : IActualMileageProvider
{
    private const int MaximumRequests = 16;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(1800);

    private static readonly Regex OdometerName = new(
        @"odometer|total distance|mileage",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Distance since …", adaptation readings etc. are not the car's total distance.
    private static readonly Regex NotTotal = new(
        @"since|after|adaptation|kiss|injector|fault|trip|range|service|reset|last|learn|regen|oil|filter|remaining|until|to empty",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DtcCatalogService _catalog;
    private readonly OBDService _obd;
    private readonly IsoTpReassembler _isoTp = new();

    public string Name => "Multi-module odometer";

    public CatalogOdometerProvider(DtcCatalogService catalog, OBDService obd)
    {
        _catalog = catalog;
        _obd = obd;
    }

    public Task<IReadOnlyList<MileageEvidence>> ReadAllAsync(
        VehicleInfo vehicle,
        CancellationToken cancellationToken = default) =>
        ReadCoreAsync(vehicle, false, cancellationToken);

    /// <summary>
    /// First module that returns a valid odometer (Service page: the current
    /// distance is enough, the comparison of modules is the Mileage page's job).
    /// </summary>
    public async Task<MileageEvidence?> ReadFirstAsync(
        VehicleInfo vehicle,
        CancellationToken cancellationToken = default) =>
        (await ReadCoreAsync(vehicle, true, cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    private async Task<IReadOnlyList<MileageEvidence>> ReadCoreAsync(
        VehicleInfo vehicle,
        bool stopAfterFirst,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(vehicle.Manufacturer))
        {
            MileageTrace.Add("მწარმოებელი უცნობია — მოდულების ოდომეტრი ვერ მოიძებნება");
            return Array.Empty<MileageEvidence>();
        }

        if (!_obd.IsInitialized)
            await _obd.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var signals = await LoadSignalsAsync(vehicle, cancellationToken).ConfigureAwait(false);
        var evidence = new List<MileageEvidence>();
        MileageTrace.Add(signals.Count == 0
            ? $"{vehicle.Manufacturer}: ოდომეტრის ცნობილი მისამართები ბაზაში არ არის"
            : $"{vehicle.Manufacturer}: {signals.Select(s => s.Key).Distinct().Count()} მოდულის მისამართი");

        // One request per module/identifier; the first signal that decodes is used.
        foreach (var group in signals
                     .GroupBy(signal => signal.Key)
                     .Take(MaximumRequests))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = group.First();

            byte[]? data;
            try
            {
                data = await ReadAsync(first, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                MileageTrace.Add($"{first.Header} {first.ServiceId:X2} {first.Identifier:X}: შეცდომა ({ex.GetType().Name})");
                continue;
            }

            if (data is null)
            {
                MileageTrace.Add($"{first.Header} {first.ServiceId:X2} {first.Identifier:X}: პასუხი არ არის");
                continue;
            }

            var decodedAny = false;

            foreach (var signal in group.OrderByDescending(item => item.IsMetric))
            {
                if (!TryDecode(signal, data, out var kilometers))
                    continue;

                evidence.Add(new MileageEvidence(
                    Name,
                    ModuleName(signal),
                    kilometers,
                    signal.ExactModel ? 0.8 : 0.7,
                    $"0x{signal.ServiceId:X2} 0x{signal.Identifier:X4} · {signal.Address.Detail} · {signal.SignalName}"));
                MileageTrace.Add($"{first.Header} {first.ServiceId:X2} {first.Identifier:X}: {kilometers:0} km ✓");
                decodedAny = true;
                break;
            }

            if (decodedAny && stopAfterFirst)
                break;

            if (!decodedAny)
                MileageTrace.Add($"{first.Header} {first.ServiceId:X2} {first.Identifier:X}: პასუხი მოვიდა, მაგრამ ვერ გაიშიფრა ({Convert.ToHexString(data.Take(8).ToArray())})");
        }

        return evidence;
    }

    // ------------------------------------------------------------------
    // Catalog
    // ------------------------------------------------------------------

    private sealed record OdometerSignal(
        string Key,
        string SignalName,
        ServiceDiagnosticAddress Address,
        int ServiceId,
        int Identifier,
        int BitOffset,
        int BitLength,
        double Mul,
        double Div,
        double Add,
        bool Miles,
        bool ExactModel,
        string Header,
        bool Meters = false)
    {
        public bool IsMetric => !Miles;
    }

    private sealed class Row
    {
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? Header { get; set; }
        public string? CommandJson { get; set; }
        public string? Name { get; set; }
        public string? Unit { get; set; }
        public int BitLength { get; set; }
        public int BitOffset { get; set; }
        public string? Eax { get; set; }
        public string? FormatJson { get; set; }
        public string? RawJson { get; set; }
    }

    private sealed class Format
    {
        public double? Mul { get; set; }
        public double? Div { get; set; }
        public double? Add { get; set; }
        public string? Unit { get; set; }
    }

    private async Task<IReadOnlyList<OdometerSignal>> LoadSignalsAsync(VehicleInfo vehicle, CancellationToken token)
    {
        await _catalog.EnsureLoadedAsync(token).ConfigureAwait(false);

        var makes = MakeFamily(vehicle.Manufacturer);
        var connection = new SQLiteAsyncConnection(_catalog.DatabasePath, SQLiteOpenFlags.ReadOnly);
        var rows = new List<Row>();
        try
        {
            foreach (var make in makes)
            {
                rows.AddRange(await connection.QueryAsync<Row>(
                    """
                    SELECT make AS Make, model AS Model, header AS Header, command_json AS CommandJson,
                           name AS Name, unit AS Unit, bit_length AS BitLength, bit_offset AS BitOffset,
                           eax AS Eax, format_json AS FormatJson, raw_json AS RawJson
                    FROM pid_definitions
                    WHERE lower(make) = lower(?)
                      AND pid IN ('22', '21')
                      AND (lower(name) LIKE '%odometer%' OR lower(name) LIKE '%total distance%' OR lower(name) LIKE '%mileage%');
                    """,
                    make).ConfigureAwait(false));
            }
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }

        var model = Normalize(vehicle.Model);
        var signals = new List<OdometerSignal>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || !OdometerName.IsMatch(row.Name) || NotTotal.IsMatch(row.Name))
                continue;
            if (row.BitLength is < 16 or > 32 || row.BitOffset < 0)
                continue;
            if (!TryParseCommand(row.CommandJson, out var serviceId, out var identifier))
                continue;
            if (!ObdbDiagnosticAddressResolver.TryResolve(row.Header, row.Eax, row.RawJson, out var address, out _))
                continue;

            var format = ParseFormat(row.FormatJson);
            var unit = (format?.Unit ?? row.Unit ?? string.Empty).ToLowerInvariant();
            if (unit is not ("kilometers" or "kilometer" or "km" or "miles" or "mile" or "meters" or "meter"))
                continue;

            var rowModel = Normalize(row.Model);
            var exact = model.Length > 0 && rowModel.Length > 0 &&
                        (rowModel == model || rowModel.Contains(model) || model.Contains(rowModel));

            signals.Add(new OdometerSignal(
                $"{serviceId:X2}|{address.TransmitCanId:X8}|{address.ExtendedAddress?.ToString("X2") ?? "--"}|{identifier:X4}",
                row.Name,
                address,
                serviceId,
                identifier,
                row.BitOffset,
                row.BitLength,
                format?.Mul ?? 1d,
                format?.Div ?? 1d,
                format?.Add ?? 0d,
                unit.StartsWith("mile", StringComparison.Ordinal),
                exact,
                (row.Header ?? string.Empty).ToUpperInvariant(),
                unit.StartsWith("meter", StringComparison.Ordinal)));
        }

        // Exact model first, then the identifiers most models of the brand
        // share (asked first because they answer on most cars).
        var shared = signals.GroupBy(signal => signal.Key).ToDictionary(group => group.Key, group => group.Count());
        return signals
            .OrderByDescending(signal => signal.ExactModel)
            .ThenByDescending(signal => shared[signal.Key])
            .ToArray();
    }

    private static bool TryParseCommand(string? json, out int serviceId, out int identifier)
    {
        serviceId = 0;
        identifier = 0;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var (service, length) in new[] { ("22", 4), ("21", 2) })
            {
                if (doc.RootElement.TryGetProperty(service, out var element) &&
                    element.ValueKind == JsonValueKind.String &&
                    element.GetString() is { } text &&
                    text.Length == length &&
                    int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out identifier))
                {
                    serviceId = service == "22" ? 0x22 : 0x21;
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static Format? ParseFormat(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Format>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Reading + decoding
    // ------------------------------------------------------------------

    private async Task<byte[]?> ReadAsync(OdometerSignal signal, CancellationToken token)
    {
        var request = signal.ServiceId == 0x21
            ? new byte[] { 0x21, (byte)signal.Identifier }
            : new byte[] { 0x22, (byte)(signal.Identifier >> 8), (byte)signal.Identifier };

        var raw = await _obd.ReadPhysicalCanReadOnlyAsync(signal.Address, request, RequestTimeout, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw) || ObdResponseParser.IsNegative(raw))
            return null;

        var positive = (byte)(signal.ServiceId + 0x40);
        var idBytes = signal.ServiceId == 0x21
            ? new[] { (byte)signal.Identifier }
            : new[] { (byte)(signal.Identifier >> 8), (byte)signal.Identifier };

        var candidates = new List<IReadOnlyList<byte[]>>();
        if (signal.Address.ReceiveCanId.HasValue)
            candidates.Add(ObdResponseParser.ParseCanFrames(raw, signal.Address.ReceiveCanId.Value));
        foreach (var id in ObdResponseParser.ExtractCanIdentifiers(raw))
            candidates.Add(ObdResponseParser.ParseCanFrames(raw, id));
        candidates.Add(ObdResponseParser.ParseCanFrames(raw));

        foreach (var frames in candidates)
        {
            if (frames.Count == 0 || frames[0].Length == 0)
                continue;

            byte[] message;
            try
            {
                message = (frames[0][0] >> 4) is 0 or 1
                    ? _isoTp.Reassemble(frames)
                    : frames.SelectMany(frame => frame).ToArray();
            }
            catch
            {
                continue;
            }

            if (Matches(message, positive, idBytes, out var data))
                return data;
        }

        // Adapter already removed the framing.
        return Matches(ObdResponseParser.ParseHex(raw), positive, idBytes, out var plain) ? plain : null;
    }

    private static bool Matches(byte[] bytes, byte positive, byte[] idBytes, out byte[] data)
    {
        data = Array.Empty<byte>();
        for (var i = 0; i + idBytes.Length < bytes.Length; i++)
        {
            if (bytes[i] != positive)
                continue;

            var ok = true;
            for (var j = 0; j < idBytes.Length && ok; j++)
                ok = bytes[i + 1 + j] == idBytes[j];

            if (!ok)
                continue;

            data = bytes[(i + 1 + idBytes.Length)..];
            return data.Length > 0;
        }

        return false;
    }

    private static bool TryDecode(OdometerSignal signal, byte[] data, out double kilometers)
    {
        kilometers = 0;
        if (signal.BitOffset + signal.BitLength > data.Length * 8 || Math.Abs(signal.Div) < double.Epsilon)
            return false;

        ulong raw = 0;
        for (var bit = 0; bit < signal.BitLength; bit++)
        {
            var absolute = signal.BitOffset + bit;
            raw = (raw << 1) | (((ulong)data[absolute / 8] >> (7 - absolute % 8)) & 1UL);
        }

        // All bits set = "not available" in most ECUs.
        if (raw == (signal.BitLength >= 64 ? ulong.MaxValue : (1UL << signal.BitLength) - 1))
            return false;

        var value = raw * signal.Mul / signal.Div + signal.Add;
        if (signal.Miles)
            value *= 1.609344d;
        else if (signal.Meters)
            value /= 1000d;

        if (!double.IsFinite(value) || value < 1 || value > 2_000_000)
            return false;

        kilometers = value;
        return true;
    }

    // ------------------------------------------------------------------
    // Names
    // ------------------------------------------------------------------

    private static string ModuleName(OdometerSignal signal)
    {
        var name = signal.SignalName.ToLowerInvariant();
        if (name.Contains("dash") || name.Contains("instrument"))
            return "Instrument cluster";
        if (name.Contains("transmission") || name.Contains("egs") || name.Contains("tcm"))
            return "Transmission";
        if (name.Contains("engine"))
            return "Engine ECU";

        return signal.Header switch
        {
            "7E0" => "Engine ECU",
            "7E1" => "Transmission",
            "714" or "720" or "7C6" or "743" or "7C0" or "6B4" => "Instrument cluster",
            _ => $"ECU {signal.Header}"
        };
    }

    private static string Normalize(string? value) =>
        new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static IReadOnlyList<string> MakeFamily(string manufacturer) =>
        CatalogMakes.For(manufacturer);
}
