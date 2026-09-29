using CarScanner.Models;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Services.Mileage;

/// <summary>
/// Odometer that the car broadcasts by itself on the CAN bus of the OBD port —
/// the same number the instrument cluster shows. Nothing is requested from any
/// module: the adapter only listens (ATMA) to one frame ID for a short time.
///
/// Frame definitions come from public sources: Open Vehicle Monitoring System
/// (GM Volt/Ampera) and comma.ai opendbc (Toyota/Lexus, Honda, Hyundai/Kia,
/// Ford, VW). Many cars have a gateway that hides these frames from the OBD
/// port; then nothing is found and other sources are used. A value is kept
/// only when two frames agree.
/// </summary>
public sealed class BroadcastOdometerProvider : IActualMileageProvider
{
    private readonly OBDService _obd;

    public BroadcastOdometerProvider(OBDService obd) => _obd = obd;

    public string Name => "CAN odometer broadcast";

    private enum Family { Gm, Toyota, Honda, HyundaiKia, Ford, Vw }

    private sealed record Profile(
        Family Family,
        string Source,
        int CanId,
        int StartBit,
        int Length,
        bool Motorola,
        double Scale,
        bool VoltOnly = false,
        int UnitStartBit = -1,
        int UnitLength = 0,
        int MilesUnitValue = -1);

    private static readonly Profile[] Profiles =
    {
        // GM Global A (Volt / Ampera): 0x120 bytes 0–3, 1/64 km (OVMS vehicle_voltampera).
        new(Family.Gm, "GM Volt CAN 0x120", 0x120, 7, 32, true, 1d / 64d, VoltOnly: true),
        // Toyota / Lexus UI_SETTING 0x611: ODOMETER 39|32@0, UNITS 26|2@0 (1 km, 2 miles).
        new(Family.Toyota, "Toyota CAN 0x611", 0x611, 39, 32, true, 1, UnitStartBit: 26, UnitLength: 2, MilesUnitValue: 2),
        // Honda ODOMETER 0x516: 7|24@0 km.
        new(Family.Honda, "Honda CAN 0x516", 0x516, 7, 24, true, 1),
        // Hyundai / Kia CLU12 0x5B0: CF_Clu_Odometer 0|24@1, 0.1 km.
        new(Family.HyundaiKia, "Hyundai/Kia CAN 0x5B0", 0x5B0, 0, 24, false, 0.1),
        // Hyundai / Kia CLU1 0x4F0 (older models): CF_Clu_Odometer 40|24@1, 0.1 km.
        new(Family.HyundaiKia, "Hyundai/Kia CAN 0x4F0", 0x4F0, 40, 24, false, 0.1),
        // Ford Cluster_Info1 0x430: OdometerMasterValue 15|24@0 km.
        new(Family.Ford, "Ford CAN 0x430", 0x430, 15, 24, true, 1),
        // VW MQB/MLB Kombi_02 0x6B7: KBI_Kilometerstand 0|20@1 km.
        new(Family.Vw, "VW CAN 0x6B7", 0x6B7, 0, 20, false, 1),
        // VW PQ Kombi_3 0x520: Kilometerstand 40|20@1 km.
        new(Family.Vw, "VW CAN 0x520", 0x520, 40, 20, false, 1)
    };

    public async Task<IReadOnlyList<MileageEvidence>> ReadAllAsync(
        VehicleInfo vehicle,
        CancellationToken cancellationToken = default)
    {
        if (!_obd.IsInitialized)
            return Array.Empty<MileageEvidence>();

        if (_obd.CurrentProtocol is not (ObdProtocol.ISO_15765_4_CAN_11BIT_500K or ObdProtocol.ISO_15765_4_CAN_11BIT_250K
                or ObdProtocol.ISO_15765_4_CAN_29BIT_500K or ObdProtocol.ISO_15765_4_CAN_29BIT_250K))
        {
            return Array.Empty<MileageEvidence>();
        }

        var family = DetectFamily(vehicle);
        if (family is null)
            return Array.Empty<MileageEvidence>();

        var isVolt = IsVolt(vehicle);
        var profiles = Profiles
            .Where(profile => profile.Family == family && (!profile.VoltOnly || isVolt))
            .ToList();
        if (profiles.Count == 0)
            return Array.Empty<MileageEvidence>();

        var evidence = new List<MileageEvidence>();
        try
        {
            await _obd.RunDtcModuleSessionAsync(async session =>
            {
                foreach (var profile in profiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var values = await ListenAsync(session, profile, cancellationToken).ConfigureAwait(false);
                    if (values.Count < 2)
                        continue;

                    // Two frames that agree (the car may move a little in between).
                    var ordered = values.OrderBy(value => value).ToList();
                    var median = ordered[ordered.Count / 2];
                    var agreeing = ordered.Count(value => Math.Abs(value - median) <= 5);
                    if (agreeing < 2)
                        continue;

                    evidence.Add(new MileageEvidence(
                        profile.Source,
                        "დაფა / CAN",
                        Math.Round(median, 1),
                        0.75,
                        $"Odometer broadcast by the car on CAN 0x{profile.CanId:X3}; {agreeing} frames agree."));
                    break;
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CAN odometer listen failed: {ex.Message}");
        }

        return evidence;
    }

    private static async Task<List<double>> ListenAsync(
        Elm327Protocol.IDtcModuleSession session,
        Profile profile,
        CancellationToken token)
    {
        var values = new List<double>();

        // 1) Monitor the bus for 2.5 s (most odometer frames repeat every 0.1–1 s).
        try
        {
            var raw = await session.MonitorBroadcastAsync(profile.CanId, false, 2500, token).ConfigureAwait(false);
            values.AddRange(Decode(raw, profile));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ATMA 0x{profile.CanId:X3} failed: {ex.Message}");
        }

        // 2) Adapters without monitoring: wait for single frames after a harmless request.
        for (var attempt = 0; values.Count < 2 && attempt < 4; attempt++)
        {
            try
            {
                var raw = await session.ListenBroadcastAsync(profile.CanId, token).ConfigureAwait(false);
                values.AddRange(Decode(raw, profile));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }

        return values;
    }

    private static IEnumerable<double> Decode(string? raw, Profile profile)
    {
        foreach (var frame in ModuleDtcScanService.ParseFrames(raw, profile.CanId, ext29: false))
        {
            if (!TryExtract(frame, profile.StartBit, profile.Length, profile.Motorola, out var rawValue))
                continue;

            var km = rawValue * profile.Scale;
            if (profile.UnitLength > 0 &&
                TryExtract(frame, profile.UnitStartBit, profile.UnitLength, true, out var unit) &&
                (long)unit == profile.MilesUnitValue)
            {
                km *= 1.609344;
            }

            if (km is >= 1 and <= 2_000_000 && double.IsFinite(km))
                yield return km;
        }
    }

    /// <summary>DBC bit extraction. Motorola (@0): start = MSB; Intel (@1): start = LSB.</summary>
    internal static bool TryExtract(byte[] data, int startBit, int length, bool motorola, out double value)
    {
        value = 0;
        if (length is < 1 or > 32 || startBit < 0)
            return false;

        ulong result = 0;
        if (motorola)
        {
            var position = startBit;
            for (var i = 0; i < length; i++)
            {
                var byteIndex = position / 8;
                if (byteIndex >= data.Length)
                    return false;
                var bit = (data[byteIndex] >> (position % 8)) & 1;
                result = (result << 1) | (uint)bit;
                position = position % 8 == 0 ? position + 15 : position - 1;
            }
        }
        else
        {
            for (var i = 0; i < length; i++)
            {
                var position = startBit + i;
                var byteIndex = position / 8;
                if (byteIndex >= data.Length)
                    return false;
                var bit = (data[byteIndex] >> (position % 8)) & 1;
                result |= (ulong)bit << i;
            }
        }

        value = result;
        return true;
    }

    private static Family? DetectFamily(VehicleInfo vehicle)
    {
        var make = (vehicle.Manufacturer ?? string.Empty).ToUpperInvariant();
        var vin = (vehicle.VIN ?? string.Empty).Trim().ToUpperInvariant();
        bool Has(params string[] names) => names.Any(name => make.Contains(name, StringComparison.Ordinal));

        if (Has("CHEVROLET", "GMC", "BUICK", "CADILLAC", "HOLDEN", "GENERAL MOTORS") ||
            (Has("OPEL", "VAUXHALL") && IsVolt(vehicle))) return Family.Gm;
        if (Has("TOYOTA", "LEXUS", "SCION")) return Family.Toyota;
        if (Has("HONDA", "ACURA")) return Family.Honda;
        if (Has("HYUNDAI", "KIA", "GENESIS")) return Family.HyundaiKia;
        if (Has("FORD", "LINCOLN", "MERCURY")) return Family.Ford;
        if (Has("VOLKSWAGEN", "AUDI", "SKODA", "ŠKODA", "SEAT", "CUPRA")) return Family.Vw;

        if (vin.Length >= 3)
        {
            var wmi = vin[..3];
            if (wmi is "1G1" or "1GC" or "1GT" or "1G4" or "1G6" or "2G1" or "3G1" or "1GN" or "1GK" or "KL1" or "KL8") return Family.Gm;
            if (wmi.StartsWith("JT", StringComparison.Ordinal) || wmi is "4T1" or "4T3" or "5TD" or "5TF" or "2T1") return Family.Toyota;
            if (wmi is "JHM" or "1HG" or "2HG" or "5FN" or "5J6" or "19X" or "JH4") return Family.Honda;
            if (wmi.StartsWith("KM", StringComparison.Ordinal) || wmi.StartsWith("KN", StringComparison.Ordinal) || wmi is "5NP" or "5XY" or "5NM") return Family.HyundaiKia;
            if (wmi.StartsWith("1F", StringComparison.Ordinal) || wmi.StartsWith("3F", StringComparison.Ordinal) || wmi == "WF0") return Family.Ford;
            if (wmi is "WVW" or "WVG" or "3VW" or "1VW" or "WAU" or "TMB" or "VSS") return Family.Vw;
        }

        return null;
    }

    private static bool IsVolt(VehicleInfo vehicle)
    {
        var model = (vehicle.Model ?? string.Empty).ToUpperInvariant();
        var vin = (vehicle.VIN ?? string.Empty).Trim().ToUpperInvariant();
        if (model.Contains("AMPERA-E") || model.Contains("AMPERA E"))
            return false;
        return model.Contains("VOLT") || model.Contains("AMPERA") ||
               (vin.Length == 17 && vin.StartsWith("1G1R", StringComparison.Ordinal));
    }
}
