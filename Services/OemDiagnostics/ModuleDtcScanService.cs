using System.Globalization;
using CarScanner.Models;
using CarScanner.Services.OBD;
using SQLite;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Reads trouble codes from the other modules of the car (ABS, airbag, body,
/// steering, transmission, cluster …), like the "all modules" scan of
/// professional apps.
///
/// Safety rules:
/// • only trouble-code READ requests are sent (UDS 19 02, KWP 18, GMLAN A9);
/// • brand-specific module addresses are used only for the brand confirmed
///   by the VIN/vehicle identity, plus the addresses listed for that brand
///   in the offline catalog; the legislated OBD range 0x7E0–0x7E7 is used
///   for every CAN car;
/// • manufacturer modules are not asked while the car is moving.
/// </summary>
public sealed class ModuleDtcScanService
{
    private readonly OBDService _obd;
    private readonly DtcCatalogService _catalog;

    public ModuleDtcScanService(OBDService obd, DtcCatalogService catalog)
    {
        _obd = obd;
        _catalog = catalog;
    }

    /// <summary>Modules of the last completed scan (for the ECU page).</summary>
    public IReadOnlyList<ModuleDtcReport> LastModules { get; private set; } = Array.Empty<ModuleDtcReport>();

    public DateTime? LastScanAt { get; private set; }

    public sealed record ScanOutcome(
        IReadOnlyList<ModuleDtcReport> Modules,
        int ModulesChecked,
        string Note);

    private sealed record Candidate(
        string Id,
        string NameKa,
        string NameEn,
        int Tx,
        int Rx,
        bool Ext29,
        int Priority,
        byte? Ext = null);

    private enum BrandFamily
    {
        Unknown,
        Gm,
        Ford,
        Mazda,
        HyundaiKia,
        Toyota,
        Honda,
        VwGroup,
        Nissan,
        Subaru,
        Stellantis,
        Chrysler,
        Renault,
        Bmw,
        Other
    }

    // ------------------------------------------------------------------
    // Known module addresses (11-bit CAN, response = listed Rx).
    // ------------------------------------------------------------------

    private static readonly (string Id, string Ka, string En)[] ModuleNames =
    {
        ("ECM", "ძრავი", "Engine"),
        ("TCM", "გადაცემათა კოლოფი", "Transmission"),
        ("ABS", "ABS / მუხრუჭები", "ABS / brakes"),
        ("SRS", "აირბაგი (SRS)", "Airbag (SRS)"),
        ("BCM", "ძარის მოდული (BCM)", "Body module (BCM)"),
        ("IPC", "ხელსაწყოების დაფა", "Instrument cluster"),
        ("EPS", "საჭის გამაძლიერებელი", "Power steering"),
        ("HVAC", "კლიმატ-კონტროლი", "Climate control"),
        ("HYB", "ჰიბრიდის სისტემა", "Hybrid system"),
        ("BMS", "მაღალი ძაბვის ბატარეა", "High-voltage battery"),
        ("GW", "გეითვეი (Gateway)", "Gateway"),
        ("RADIO", "მულტიმედია", "Infotainment"),
        ("ADAS", "რადარი / კამერა", "Radar / camera"),
        ("KEY", "გასაღები / იმობილაიზერი", "Key / immobilizer"),
        ("PARK", "პარკინგის სისტემა", "Parking system"),
        ("OBD", "OBD მოდული", "OBD module"),
        ("MOD", "მოდული", "Module")
    };

    private static readonly (int Tx, int Rx, string Id)[] StandardObd =
    {
        (0x7E0, 0x7E8, "ECM"),
        (0x7E1, 0x7E9, "TCM"),
        (0x7E2, 0x7EA, "OBD"),
        (0x7E3, 0x7EB, "OBD"),
        (0x7E4, 0x7EC, "OBD"),
        (0x7E5, 0x7ED, "OBD"),
        (0x7E6, 0x7EE, "OBD"),
        (0x7E7, 0x7EF, "OBD")
    };

    private static readonly Dictionary<BrandFamily, (int Tx, int Rx, string Id)[]> BrandModules = new()
    {
        [BrandFamily.Gm] = new[]
        {
            (0x7E2, 0x7EA, "TCM"),
            (0x7E4, 0x7EC, "HYB"),
            (0x7E7, 0x7EF, "BMS"),
            (0x241, 0x641, "BCM"),
            (0x242, 0x642, "MOD"),
            (0x243, 0x643, "ABS"),
            (0x244, 0x644, "MOD"),
            (0x245, 0x645, "MOD"),
            (0x246, 0x646, "MOD"),
            (0x247, 0x647, "SRS"),
            (0x248, 0x648, "MOD"),
            (0x249, 0x649, "MOD"),
            (0x24A, 0x64A, "MOD"),
            (0x24B, 0x64B, "MOD"),
            (0x24C, 0x64C, "MOD"),
            (0x24D, 0x64D, "MOD"),
            (0x24E, 0x64E, "MOD"),
            (0x24F, 0x64F, "MOD"),
            (0x250, 0x650, "MOD"),
            (0x251, 0x651, "HVAC"),
            (0x252, 0x652, "MOD"),
            (0x253, 0x653, "MOD"),
            (0x254, 0x654, "MOD"),
            (0x255, 0x655, "MOD"),
            (0x256, 0x656, "MOD"),
            (0x257, 0x657, "MOD"),
            (0x258, 0x658, "MOD"),
            (0x259, 0x659, "MOD"),
            (0x25A, 0x65A, "MOD"),
            (0x25B, 0x65B, "MOD"),
            (0x25C, 0x65C, "MOD"),
            (0x25D, 0x65D, "MOD")
        },
        [BrandFamily.Ford] = new[]
        {
            (0x760, 0x768, "ABS"),
            (0x737, 0x73F, "SRS"),
            (0x726, 0x72E, "BCM"),
            (0x720, 0x728, "IPC"),
            (0x730, 0x738, "EPS"),
            (0x733, 0x73B, "HVAC"),
            (0x716, 0x71E, "GW"),
            (0x7D0, 0x7D8, "RADIO"),
            (0x727, 0x72F, "RADIO"),
            (0x706, 0x70E, "ADAS"),
            (0x764, 0x76C, "ADAS"),
            (0x736, 0x73E, "PARK"),
            (0x724, 0x72C, "MOD"),
            (0x740, 0x748, "MOD"),
            (0x741, 0x749, "MOD")
        },
        [BrandFamily.Mazda] = new[]
        {
            (0x760, 0x768, "ABS"),
            (0x737, 0x73F, "SRS"),
            (0x726, 0x72E, "BCM"),
            (0x720, 0x728, "IPC"),
            (0x730, 0x738, "EPS"),
            (0x733, 0x73B, "HVAC"),
            (0x7D0, 0x7D8, "RADIO"),
            (0x706, 0x70E, "ADAS"),
            (0x764, 0x76C, "ADAS")
        },
        [BrandFamily.HyundaiKia] = new[]
        {
            (0x7D1, 0x7D9, "ABS"),
            (0x7D2, 0x7DA, "SRS"),
            (0x7D4, 0x7DC, "EPS"),
            (0x7A0, 0x7A8, "BCM"),
            (0x770, 0x778, "BCM"),
            (0x7C6, 0x7CE, "IPC"),
            (0x7B3, 0x7BB, "HVAC"),
            (0x7C4, 0x7CC, "ADAS"),
            (0x7D0, 0x7D8, "ADAS"),
            (0x7A5, 0x7AD, "KEY"),
            (0x7C7, 0x7CF, "ADAS")
        },
        [BrandFamily.Toyota] = new[]
        {
            (0x7B0, 0x7B8, "ABS"),
            (0x780, 0x788, "SRS"),
            (0x7A1, 0x7A9, "EPS"),
            (0x7C0, 0x7C8, "IPC"),
            (0x7C4, 0x7CC, "HVAC"),
            (0x791, 0x799, "HYB"),
            (0x781, 0x789, "PARK"),
            (0x750, 0x758, "GW"),
            (0x790, 0x798, "ADAS")
        },
        [BrandFamily.VwGroup] = new[]
        {
            (0x713, 0x77D, "ABS"),
            (0x715, 0x77F, "SRS"),
            (0x712, 0x77C, "EPS"),
            (0x710, 0x77A, "GW"),
            (0x714, 0x77E, "IPC"),
            (0x70E, 0x778, "BCM"),
            (0x746, 0x7B0, "HVAC"),
            (0x773, 0x7DD, "RADIO"),
            (0x757, 0x7C1, "ADAS"),
            (0x74F, 0x7B9, "ADAS"),
            (0x70A, 0x774, "PARK")
        },
        [BrandFamily.Nissan] = new[]
        {
            (0x740, 0x760, "ABS"),
            (0x752, 0x772, "SRS"),
            (0x745, 0x765, "BCM"),
            (0x743, 0x763, "IPC"),
            (0x742, 0x762, "EPS"),
            (0x744, 0x764, "HVAC"),
            (0x746, 0x766, "KEY")
        },
        [BrandFamily.Subaru] = new[]
        {
            (0x7B0, 0x7B8, "ABS"),
            (0x746, 0x74E, "EPS"),
            (0x780, 0x788, "SRS")
        }
    };

    /// <summary>BMW / MINI (F/G series): tester 0x6F1, target address in the first data byte, reply on 0x600 + target.</summary>
    private static readonly (byte Target, string Id)[] BmwModules =
    {
        (0x12, "ECM"),
        (0x13, "ECM"),
        (0x18, "TCM"),
        (0x29, "ABS"),
        (0x01, "SRS"),
        (0x30, "EPS"),
        (0x40, "BCM"),
        (0x72, "BCM"),
        (0x60, "IPC"),
        (0x63, "RADIO"),
        (0x78, "HVAC"),
        (0x10, "GW")
    };

    // ------------------------------------------------------------------

    public async Task<ScanOutcome> ScanAsync(
        VehicleInfo vehicle,
        double? vehicleSpeedKph,
        IProgress<(double Fraction, string Text)>? progress,
        CancellationToken token)
    {
        if (!_obd.IsInitialized)
            return new ScanOutcome(Array.Empty<ModuleDtcReport>(), 0, "OBD adapter არ არის მზად.");

        var protocol = _obd.CurrentProtocol;
        if (protocol is not (ObdProtocol.ISO_15765_4_CAN_11BIT_500K
                or ObdProtocol.ISO_15765_4_CAN_11BIT_250K
                or ObdProtocol.ISO_15765_4_CAN_29BIT_500K
                or ObdProtocol.ISO_15765_4_CAN_29BIT_250K))
        {
            return new ScanOutcome(Array.Empty<ModuleDtcReport>(), 0,
                "ამ მანქანის კავშირის ტიპზე (არა CAN) სხვა მოდულების სკანი ჯერ არ არის ხელმისაწვდომი.");
        }

        var is29BitCar = protocol is ObdProtocol.ISO_15765_4_CAN_29BIT_500K
            or ObdProtocol.ISO_15765_4_CAN_29BIT_250K;
        var family = DetectFamily(vehicle);
        var carMoving = vehicleSpeedKph is > 0.5;

        var candidates = new List<Candidate>();
        AddStandard(candidates, is29BitCar);

        if (!carMoving)
        {
            if (BrandModules.TryGetValue(family, out var table))
            {
                foreach (var (tx, rx, id) in table)
                    candidates.Add(Make(id, tx, rx, false, PriorityOf(id)));
            }

            if (family == BrandFamily.Bmw && !is29BitCar)
            {
                foreach (var (target, id) in BmwModules)
                    candidates.Add(Make(id, 0x6F1, 0x600 + target, false, PriorityOf(id)) with { Ext = target });
            }

            foreach (var candidate in await LoadCatalogCandidatesAsync(family, token).ConfigureAwait(false))
                candidates.Add(candidate);
        }

        // One request per module address; brand/catalog names win over "OBD"/"MOD".
        var unique = candidates
            .GroupBy(item => (item.Tx, item.Ext29, item.Ext))
            .Select(group => group
                .OrderBy(item => item.Id is "OBD" or "MOD" ? 1 : 0)
                .ThenBy(item => item.Priority)
                .First())
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.Tx)
            .Take(48)
            .ToList();

        var results = new List<ModuleDtcReport>();
        var checkedCount = 0;

        try
        {
            await _obd.RunDtcModuleSessionAsync(async session =>
            {
                for (var index = 0; index < unique.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var candidate = unique[index];
                    progress?.Report(((double)index / Math.Max(1, unique.Count),
                        $"მოდულების შემოწმება: {candidate.NameKa} ({index + 1}/{unique.Count})"));

                    checkedCount++;
                    ModuleDtcReport? report = null;
                    try
                    {
                        report = await ReadModuleAsync(session, candidate, family, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Module {candidate.Tx:X} DTC read failed: {ex.Message}");
                    }

                    if (report is not null)
                        results.Add(report);
                }
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Module DTC session failed: {ex}");
            if (results.Count == 0)
                return new ScanOutcome(results, checkedCount, "სხვა მოდულების შემოწმება ვერ მოხერხდა ამ ადაპტერით.");
        }

        await DescribeAsync(results, token).ConfigureAwait(false);
        progress?.Report((1d, "მოდულების შემოწმება დასრულდა."));
        LastModules = results.Where(item => item.Responded).ToList();
        LastScanAt = DateTime.Now;

        var note = carMoving
            ? "მანქანა მოძრაობდა, ამიტომ შემოწმდა მხოლოდ ძრავი და კოლოფი. სრული სკანისთვის გააჩერე მანქანა."
            : family == BrandFamily.Unknown && results.Count(item => item.Responded) <= 1
                ? "მარკა ვერ დადგინდა, ამიტომ მხოლოდ სტანდარტული მოდულები შემოწმდა."
                : string.Empty;

        return new ScanOutcome(results, checkedCount, note);
    }

    /// <summary>
    /// Clears the trouble codes of the given modules (UDS/KWP 0x14, GMLAN 0x04).
    /// Returns how many modules confirmed the clear.
    /// </summary>
    public async Task<int> ClearAsync(IReadOnlyList<ModuleDtcReport> modules, CancellationToken token) =>
        (await ClearDetailedAsync(modules, token).ConfigureAwait(false)).Count(result => result.Confirmed);

    /// <summary>Result of a clear request to one module.</summary>
    public sealed record ModuleClearResult(ModuleDtcReport Module, bool Confirmed, byte? Nrc, string Error);

    /// <summary>
    /// Clears the trouble codes of the given modules and reports, per module,
    /// whether it confirmed (positive reply) or refused (negative response code).
    /// </summary>
    public async Task<IReadOnlyList<ModuleClearResult>> ClearDetailedAsync(IReadOnlyList<ModuleDtcReport> modules, CancellationToken token)
    {
        var results = new List<ModuleClearResult>();
        if (modules.Count == 0 || !_obd.IsInitialized)
            return results;

        await _obd.RunDtcModuleSessionAsync(async session =>
        {
            foreach (var module in modules)
            {
                token.ThrowIfCancellationRequested();
                byte[]? request = module.Protocol switch
                {
                    "UDS" => new byte[] { 0x14, 0xFF, 0xFF, 0xFF },
                    "KWP" => new byte[] { 0x14, 0xFF, 0x00 },
                    "GMLAN" => new byte[] { 0x04 },
                    _ => null
                };
                if (request is null)
                {
                    results.Add(new ModuleClearResult(module, false, null, "unsupported"));
                    continue;
                }

                try
                {
                    var candidate = new Candidate(module.ModuleId, module.NameKa, module.NameEn,
                        module.TransmitCanId, module.ReceiveCanId, module.Extended29Bit, 0, module.ExtendedAddress);
                    var reply = await SendIsoTpAsync(session, candidate, request, token).ConfigureAwait(false);
                    results.Add(reply.Positive is not null
                        ? new ModuleClearResult(module, true, null, string.Empty)
                        : new ModuleClearResult(module, false, reply.Nrc, reply.AnyReply ? "refused" : "no answer"));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Module {module.TransmitCanId:X} clear failed: {ex.Message}");
                    results.Add(new ModuleClearResult(module, false, null, ex.Message));
                }
            }
        }, token).ConfigureAwait(false);

        return results;
    }

    // ------------------------------------------------------------------
    // Per-module read
    // ------------------------------------------------------------------

    private static async Task<ModuleDtcReport?> ReadModuleAsync(
        Elm327Protocol.IDtcModuleSession session,
        Candidate candidate,
        BrandFamily family,
        CancellationToken token)
    {
        var report = new ModuleDtcReport
        {
            ModuleId = candidate.Id,
            NameKa = candidate.NameKa,
            NameEn = candidate.NameEn,
            TransmitCanId = candidate.Tx,
            ReceiveCanId = candidate.Rx,
            Extended29Bit = candidate.Ext29,
            ExtendedAddress = candidate.Ext
        };

        // 1) UDS ReadDTCInformation — reportDTCByStatusMask (all bits).
        var uds = await SendIsoTpAsync(session, candidate, new byte[] { 0x19, 0x02, 0xFF }, token).ConfigureAwait(false);
        if (uds.Positive is { } udsMessage && udsMessage.Length >= 3 && udsMessage[0] == 0x59 && udsMessage[1] == 0x02)
        {
            report.Protocol = "UDS";
            report.Responded = true;
            report.Codes = ParseUds(udsMessage);
            await IdentifyUdsAsync(session, candidate, report, token).ConfigureAwait(false);
            await ReadUdsSnapshotsAsync(session, candidate, report, family, token).ConfigureAwait(false);
            return report;
        }

        if (!uds.AnyReply)
            return null;

        // 2) KWP2000 ReadDTCByStatus (older Hyundai/Kia, Toyota …).
        var kwp = await SendIsoTpAsync(session, candidate, new byte[] { 0x18, 0x02, 0xFF, 0x00 }, token).ConfigureAwait(false);
        if (kwp.Positive is { } kwpMessage && kwpMessage.Length >= 2 && kwpMessage[0] == 0x58)
        {
            report.Protocol = "KWP";
            report.Responded = true;
            report.Codes = ParseKwp(kwpMessage);
            return report;
        }

        // 3) GM (GMLAN) $A9 — reply comes as unsegmented frames on 0x5xx.
        if (family == BrandFamily.Gm && !candidate.Ext29)
        {
            try
            {
                var uudt = 0x500 | (candidate.Rx & 0xFF);
                var raw = await session.SendGmlanDtcRequestAsync(candidate.Tx, uudt, new byte[] { 0xA9, 0x81, 0x12 }, token).ConfigureAwait(false);
                var frames = ParseFrames(raw, uudt, ext29: false);
                if (frames.Count > 0)
                {
                    report.Protocol = "GMLAN";
                    report.Responded = true;
                    report.Codes = ParseGmlan(frames);
                    await IdentifyGmlanAsync(session, candidate, report, token).ConfigureAwait(false);
                    return report;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Some adapters cannot send raw (unformatted) CAN frames.
                System.Diagnostics.Debug.WriteLine($"GMLAN A9 {candidate.Tx:X} failed: {ex.Message}");
            }
        }

        // The module answers but does not report trouble codes this way.
        report.Protocol = "—";
        report.Responded = true;
        report.Codes = new List<ModuleDtc>();
        return report;
    }

    private sealed record IsoTpReply(byte[]? Positive, bool AnyReply, byte? Nrc = null);

    private static async Task<IsoTpReply> SendIsoTpAsync(
        Elm327Protocol.IDtcModuleSession session,
        Candidate candidate,
        byte[] request,
        CancellationToken token)
    {
        var raw = await session.SendAsync(candidate.Tx, candidate.Rx, candidate.Ext29, request, longWait: false, token, candidate.Ext).ConfigureAwait(false);
        var messages = Reassemble(StripExtended(ParseFrames(raw, candidate.Rx, candidate.Ext29), candidate.Ext));
        var service = request[0];

        // 7F xx 78 = "response pending": listen once more with a longer window.
        if (messages.Count > 0 &&
            messages.All(message => IsPending(message, service)))
        {
            raw = await session.SendAsync(candidate.Tx, candidate.Rx, candidate.Ext29, request, longWait: true, token, candidate.Ext).ConfigureAwait(false);
            messages = Reassemble(StripExtended(ParseFrames(raw, candidate.Rx, candidate.Ext29), candidate.Ext));
        }

        var positive = messages.LastOrDefault(message => message.Length > 0 && message[0] == (byte)(service + 0x40));
        var negative = messages.LastOrDefault(message =>
            message.Length >= 3 && message[0] == 0x7F && message[1] == service && message[2] != 0x78);
        return new IsoTpReply(positive, messages.Count > 0, positive is null ? negative?[2] : null);
    }

    private static bool IsPending(byte[] message, byte service) =>
        message.Length >= 3 && message[0] == 0x7F && message[1] == service && message[2] == 0x78;

    // ------------------------------------------------------------------
    // Identification (module name / software version)
    // ------------------------------------------------------------------

    private static async Task IdentifyUdsAsync(
        Elm327Protocol.IDtcModuleSession session,
        Candidate candidate,
        ModuleDtcReport report,
        CancellationToken token)
    {
        try
        {
            // F197 systemName, then software version identifiers.
            report.EcuName = await ReadAsciiDidAsync(session, candidate, 0xF197, token).ConfigureAwait(false);
            AddIdentity(report, "სისტემის სახელი", "System name", report.EcuName);
            foreach (var did in new[] { 0xF189, 0xF195, 0xF100, 0xF188 })
            {
                var version = await ReadAsciiDidAsync(session, candidate, did, token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    report.SoftwareVersion = version;
                    break;
                }
            }

            // Full identification (ECU page): each identifier is asked once, read-only.
            foreach (var (did, ka, en) in UdsIdentityDids)
            {
                var value = await ReadAsciiDidAsync(session, candidate, did, token).ConfigureAwait(false);
                AddIdentity(report, ka, en, value);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        ApplyReportedName(report);
    }

    private static readonly (int Did, string Ka, string En)[] UdsIdentityDids =
    {
        (0xF190, "VIN", "VIN"),
        (0xF187, "ნაწილის ნომერი", "Spare part number"),
        (0xF188, "პროგრამის ნომერი", "Software number"),
        (0xF189, "პროგრამის ვერსია", "Software version"),
        (0xF191, "აპარატურის ნომერი", "Hardware number"),
        (0xF193, "აპარატურის ვერსია", "Hardware version"),
        (0xF195, "მომწოდებლის პროგრამის ვერსია", "Supplier software version"),
        (0xF18A, "მომწოდებელი", "Supplier"),
        (0xF18C, "სერიული ნომერი", "Serial number")
    };

    private static void AddIdentity(ModuleDtcReport report, string ka, string en, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (report.Identity.Any(item => item.NameEn == en || item.Value == value && item.NameEn != "VIN"))
            return;
        report.Identity.Add(new ModuleSnapshotValue { NameKa = ka, NameEn = en, Value = value.Trim() });
    }

    private static async Task<byte[]?> ReadGmlanDidAsync(
        Elm327Protocol.IDtcModuleSession session,
        Candidate candidate,
        byte did,
        CancellationToken token)
    {
        var reply = await SendIsoTpAsync(session, candidate, new byte[] { 0x1A, did }, token).ConfigureAwait(false);
        return reply.Positive is { Length: > 2 } message && message[1] == did ? message[2..] : null;
    }

    private static string GmlanNumber(byte[]? data)
    {
        if (data is not { Length: >= 4 })
            return string.Empty;
        var number = ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];
        return number is 0 or 0xFFFFFFFF ? string.Empty : number.ToString(CultureInfo.InvariantCulture);
    }

    private static string AsciiText(byte[]? data)
    {
        if (data is null || data.Length == 0)
            return string.Empty;
        var text = new string(data.Select(b => b is >= 0x20 and < 0x7F ? (char)b : ' ').ToArray()).Trim();
        var letters = text.Count(char.IsLetterOrDigit);
        return letters >= 2 && letters * 2 >= text.Length ? text : string.Empty;
    }

    private static async Task IdentifyGmlanAsync(
        Elm327Protocol.IDtcModuleSession session,
        Candidate candidate,
        ModuleDtcReport report,
        CancellationToken token)
    {
        try
        {
            // $1A $C1: software part number of the main software module (4 bytes).
            var software = GmlanNumber(await ReadGmlanDidAsync(session, candidate, 0xC1, token).ConfigureAwait(false));
            if (!string.IsNullOrWhiteSpace(software))
                report.SoftwareVersion = software;
            AddIdentity(report, "პროგრამის ნომერი", "Software part number", software);

            // Other GMLAN identifiers (ECU page): VIN, end/base model part numbers,
            // traceability (serial) and programming date.
            AddIdentity(report, "VIN", "VIN", AsciiText(await ReadGmlanDidAsync(session, candidate, 0x90, token).ConfigureAwait(false)));
            AddIdentity(report, "ნაწილის ნომერი", "End model part number", GmlanNumber(await ReadGmlanDidAsync(session, candidate, 0xCB, token).ConfigureAwait(false)));
            AddIdentity(report, "საბაზო ნაწილის ნომერი", "Base model part number", GmlanNumber(await ReadGmlanDidAsync(session, candidate, 0xCC, token).ConfigureAwait(false)));
            AddIdentity(report, "სერიული ნომერი", "Serial number", AsciiText(await ReadGmlanDidAsync(session, candidate, 0xB4, token).ConfigureAwait(false)));
            var date = await ReadGmlanDidAsync(session, candidate, 0x99, token).ConfigureAwait(false);
            if (date is { Length: >= 4 })
            {
                var text = $"{date[0]:X2}{date[1]:X2}-{date[2]:X2}-{date[3]:X2}";
                if (System.Text.RegularExpressions.Regex.IsMatch(text, "^(19|20)\\d\\d-(0[1-9]|1[0-2])-(0[1-9]|[12]\\d|3[01])$"))
                    AddIdentity(report, "დაპროგრამების თარიღი", "Programming date", text);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }
    }

    private static async Task<string> ReadAsciiDidAsync(
        Elm327Protocol.IDtcModuleSession session,
        Candidate candidate,
        int did,
        CancellationToken token)
    {
        var reply = await SendIsoTpAsync(session, candidate, new byte[] { 0x22, (byte)(did >> 8), (byte)did }, token).ConfigureAwait(false);
        if (reply.Positive is not { Length: > 3 } message || message[1] != (byte)(did >> 8) || message[2] != (byte)did)
            return string.Empty;

        var text = new string(message.Skip(3)
            .Select(b => b is >= 0x20 and < 0x7F ? (char)b : ' ')
            .ToArray()).Trim();
        text = System.Text.RegularExpressions.Regex.Replace(text, "\\s{2,}", " ");
        // Mostly-printable text only; binary identifiers are ignored.
        var letters = text.Count(char.IsLetterOrDigit);
        return letters >= 2 && letters * 2 >= text.Length ? text : string.Empty;
    }

    /// <summary>Uses the module's own name when our table only knows an address.</summary>
    private static void ApplyReportedName(ModuleDtcReport report)
    {
        if (string.IsNullOrWhiteSpace(report.EcuName) || report.ModuleId is not ("MOD" or "OBD"))
            return;

        var upper = report.EcuName.ToUpperInvariant();
        var mapped = upper switch
        {
            _ when upper.Contains("ABS") || upper.Contains("ESP") || upper.Contains("ESC") || upper.Contains("BRAKE") => "ABS",
            _ when upper.Contains("AIRBAG") || upper.Contains("SRS") || upper.Contains("RESTRAINT") => "SRS",
            _ when upper.Contains("STEER") || upper.Contains("EPS") || upper.Contains("MDPS") => "EPS",
            _ when upper.Contains("CLIMA") || upper.Contains("HVAC") || upper.Contains("AIR CON") => "HVAC",
            _ when upper.Contains("INSTR") || upper.Contains("CLUSTER") || upper.Contains("KOMBI") || upper.Contains("DASH") => "IPC",
            _ when upper.Contains("GATEWAY") => "GW",
            _ when upper.Contains("RADIO") || upper.Contains("AUDIO") || upper.Contains("INFOTAIN") || upper.Contains("NAVI") || upper.Contains("MMI") => "RADIO",
            _ when upper.Contains("PARK") || upper.Contains("PDC") => "PARK",
            _ when upper.Contains("CAMERA") || upper.Contains("RADAR") || upper.Contains("ACC") || upper.Contains("LANE") => "ADAS",
            _ when upper.Contains("TRANS") || upper.Contains("GEARBOX") || upper.Contains("TCM") || upper.Contains("DSG") => "TCM",
            _ when upper.Contains("ENGINE") || upper.Contains("ECM") || upper.Contains("MOTOR") => "ECM",
            _ when upper.Contains("BODY") || upper.Contains("BCM") || upper.Contains("COMFORT") => "BCM",
            _ when upper.Contains("KEY") || upper.Contains("IMMO") || upper.Contains("SMART") => "KEY",
            _ when upper.Contains("BATTERY") || upper.Contains("BMS") => "BMS",
            _ => null
        };

        if (mapped is not null)
        {
            var name = ModuleNames.First(item => item.Id == mapped);
            report.ModuleId = mapped;
            report.NameKa = name.Ka;
            report.NameEn = name.En;
        }
        else
        {
            report.NameKa = report.EcuName;
            report.NameEn = report.EcuName;
        }
    }

    // ------------------------------------------------------------------
    // Freeze frame (UDS 19 04 snapshot records)
    // ------------------------------------------------------------------

    private static async Task ReadUdsSnapshotsAsync(
        Elm327Protocol.IDtcModuleSession session,
        Candidate candidate,
        ModuleDtcReport report,
        BrandFamily family,
        CancellationToken token)
    {
        foreach (var code in report.Codes.Take(3))
        {
            try
            {
                var dtc = EncodeCode(code.Code);
                if (dtc is null)
                    continue;

                var request = new byte[] { 0x19, 0x04, dtc.Value.High, dtc.Value.Middle, code.FailureType, 0xFF };
                var reply = await SendIsoTpAsync(session, candidate, request, token).ConfigureAwait(false);
                if (reply.Positive is { } message && message.Length > 6 && message[1] == 0x04)
                    code.Snapshot = DecodeSnapshot(message, family == BrandFamily.Ford || family == BrandFamily.Mazda);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }
    }

    internal static (byte High, byte Middle)? EncodeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 5)
            return null;

        var type = char.ToUpperInvariant(code[0]) switch { 'P' => 0, 'C' => 1, 'B' => 2, 'U' => 3, _ => -1 };
        if (type < 0 ||
            !int.TryParse(code.AsSpan(1, 1), out var second) || second > 3 ||
            !int.TryParse(code.AsSpan(2, 3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rest))
            return null;

        var high = (byte)((type << 6) | (second << 4) | ((rest >> 8) & 0x0F));
        return (high, (byte)(rest & 0xFF));
    }

    /// <summary>
    /// 59 04 DTC(3) status { record, count, { DID(2) data }* }*. Only data
    /// identifiers with a known standard layout are decoded; decoding stops
    /// at the first manufacturer-specific identifier of a record.
    /// </summary>
    internal static List<ModuleSnapshotValue> DecodeSnapshot(byte[] message, bool fordStyle)
    {
        var values = new List<ModuleSnapshotValue>();
        var index = 6;
        while (index + 1 < message.Length)
        {
            index++; // record number
            if (index >= message.Length)
                break;
            var count = message[index++];
            var stopRecord = false;
            for (var n = 0; n < count && index + 1 < message.Length; n++)
            {
                var did = (message[index] << 8) | message[index + 1];
                index += 2;
                if (!TryDecodeSnapshotDid(did, message, index, fordStyle, out var length, out var value))
                {
                    stopRecord = true;
                    break;
                }

                index += length;
                if (value is not null && values.All(item => item.NameEn != value.NameEn))
                    values.Add(value);
            }

            if (stopRecord)
                break;
        }

        return values;
    }

    private static bool TryDecodeSnapshotDid(int did, byte[] data, int offset, bool fordStyle, out int length, out ModuleSnapshotValue? value)
    {
        value = null;
        length = 0;

        (int Length, string Ka, string En, Func<byte[], int, string> Format)? layout = did switch
        {
            0xF404 => (1, "დატვირთვა", "Engine load", (d, o) => $"{d[o] * 100 / 255} %"),
            0xF405 => (1, "გამაგრილებლის ტემპ.", "Coolant temp", (d, o) => $"{d[o] - 40} °C"),
            0xF406 => (1, "საწვავის კორექცია (მოკლე)", "Short fuel trim", (d, o) => $"{(d[o] - 128) * 100 / 128.0:0.#} %"),
            0xF407 => (1, "საწვავის კორექცია (გრძელი)", "Long fuel trim", (d, o) => $"{(d[o] - 128) * 100 / 128.0:0.#} %"),
            0xF40B => (1, "კოლექტორის წნევა", "Manifold pressure", (d, o) => $"{d[o]} kPa"),
            0xF40C => (2, "ბრუნები", "Engine RPM", (d, o) => $"{((d[o] << 8) | d[o + 1]) / 4} rpm"),
            0xF40D => (1, "სიჩქარე", "Speed", (d, o) => $"{d[o]} km/h"),
            0xF40E => (1, "ანთების წინსწრება", "Timing advance", (d, o) => $"{d[o] / 2.0 - 64:0.#} °"),
            0xF40F => (1, "შემავალი ჰაერის ტემპ.", "Intake air temp", (d, o) => $"{d[o] - 40} °C"),
            0xF410 => (2, "ჰაერის ნაკადი", "Air flow", (d, o) => $"{((d[o] << 8) | d[o + 1]) / 100.0:0.#} g/s"),
            0xF411 => (1, "დროსელი", "Throttle", (d, o) => $"{d[o] * 100 / 255} %"),
            0xF41F => (2, "ძრავის მუშაობის დრო", "Run time", (d, o) => $"{(d[o] << 8) | d[o + 1]} s"),
            0xF421 => (2, "გავლილი Check Engine-ით", "Distance with MIL", (d, o) => $"{(d[o] << 8) | d[o + 1]} km"),
            0xF42F => (1, "საწვავის დონე", "Fuel level", (d, o) => $"{d[o] * 100 / 255} %"),
            0xF431 => (2, "გავლილი წაშლის შემდეგ", "Distance since clear", (d, o) => $"{(d[o] << 8) | d[o + 1]} km"),
            0xF433 => (1, "ატმოსფერული წნევა", "Barometric pressure", (d, o) => $"{d[o]} kPa"),
            0xF442 => (2, "ძაბვა", "Voltage", (d, o) => $"{((d[o] << 8) | d[o + 1]) / 1000.0:0.0} V"),
            0xF446 => (1, "გარე ტემპერატურა", "Ambient temp", (d, o) => $"{d[o] - 40} °C"),
            0xF45C => (1, "ზეთის ტემპერატურა", "Oil temp", (d, o) => $"{d[o] - 40} °C"),
            0xDD01 when fordStyle => (3, "გარბენი", "Odometer", (d, o) => $"{(d[o] << 16) | (d[o + 1] << 8) | d[o + 2]} km"),
            0xDD02 when fordStyle => (1, "ძაბვა", "Voltage", (d, o) => $"{d[o] / 10.0:0.0} V"),
            0xDD05 when fordStyle => (1, "გარე ტემპერატურა", "Ambient temp", (d, o) => $"{d[o] - 40} °C"),
            0xDD00 when fordStyle => (4, string.Empty, string.Empty, (_, _) => string.Empty),
            _ => null
        };

        if (layout is null)
            return false;

        length = layout.Value.Length;
        if (offset + length > data.Length)
            return false;

        if (!string.IsNullOrEmpty(layout.Value.En))
        {
            value = new ModuleSnapshotValue
            {
                NameKa = layout.Value.Ka,
                NameEn = layout.Value.En,
                Value = layout.Value.Format(data, offset)
            };
        }

        return true;
    }

    internal static List<byte[]> StripExtended(List<byte[]> frames, byte? extendedAddress)
    {
        if (!extendedAddress.HasValue)
            return frames;

        // Replies of BMW-style modules start with the tester address (0xF1).
        return frames
            .Select(frame => frame.Length > 1 && frame[0] == 0xF1 ? frame[1..] : frame)
            .ToList();
    }

    // ------------------------------------------------------------------
    // Parsing
    // ------------------------------------------------------------------

    /// <summary>Frames of one CAN ID from the adapter output ("7E8037F1911" tokens with spaces off).</summary>
    internal static List<byte[]> ParseFrames(string? raw, int receiveId, bool ext29)
    {
        var frames = new List<byte[]>();
        if (string.IsNullOrWhiteSpace(raw))
            return frames;

        var headerLength = ext29 ? 8 : 3;
        var header = receiveId.ToString(ext29 ? "X8" : "X3");
        foreach (var token in raw.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var text = token.Trim().ToUpperInvariant();
            if (text.Length <= headerLength || (text.Length - headerLength) % 2 != 0)
                continue;
            if (!text.StartsWith(header, StringComparison.Ordinal))
                continue;
            if (!text.All(Uri.IsHexDigit))
                continue;

            var data = new byte[(text.Length - headerLength) / 2];
            for (var i = 0; i < data.Length; i++)
                data[i] = byte.Parse(text.AsSpan(headerLength + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            frames.Add(data);
        }

        return frames;
    }

    /// <summary>ISO-TP messages from frames (single, first + consecutive).</summary>
    internal static List<byte[]> Reassemble(IReadOnlyList<byte[]> frames)
    {
        var messages = new List<byte[]>();
        List<byte>? buffer = null;
        var expected = 0;
        var sequence = 0;

        foreach (var frame in frames)
        {
            if (frame.Length == 0)
                continue;

            var type = frame[0] >> 4;
            switch (type)
            {
                case 0:
                {
                    var length = frame[0] & 0x0F;
                    if (length > 0 && length <= frame.Length - 1)
                        messages.Add(frame.Skip(1).Take(length).ToArray());
                    buffer = null;
                    break;
                }
                case 1 when frame.Length >= 2:
                {
                    expected = ((frame[0] & 0x0F) << 8) | frame[1];
                    buffer = new List<byte>(expected);
                    buffer.AddRange(frame.Skip(2));
                    sequence = 1;
                    break;
                }
                case 2 when buffer is not null:
                {
                    if ((frame[0] & 0x0F) != (sequence & 0x0F))
                    {
                        buffer = null;
                        break;
                    }

                    buffer.AddRange(frame.Skip(1));
                    sequence++;
                    break;
                }
            }

            if (buffer is not null && expected > 0 && buffer.Count >= expected)
            {
                messages.Add(buffer.Take(expected).ToArray());
                buffer = null;
            }
        }

        return messages;
    }

    internal static List<ModuleDtc> ParseUds(byte[] message)
    {
        // 59 02 <availability mask> [DTC high, DTC middle, failure type, status] …
        var codes = new List<ModuleDtc>();
        for (var i = 3; i + 3 < message.Length; i += 4)
        {
            var high = message[i];
            var middle = message[i + 1];
            var failureType = message[i + 2];
            var status = message[i + 3];
            if (high == 0 && middle == 0 && failureType == 0)
                continue;

            // testFailed / pending / confirmed. Codes that only "have not run yet" are skipped.
            if ((status & 0x0D) == 0)
                continue;

            codes.Add(new ModuleDtc
            {
                Code = FormatCode(high, middle),
                FailureType = failureType,
                StatusByte = status,
                Active = (status & 0x01) != 0,
                Pending = (status & 0x04) != 0,
                Confirmed = (status & 0x08) != 0
            });
        }

        return Distinct(codes);
    }

    internal static List<ModuleDtc> ParseKwp(byte[] message)
    {
        // 58 <count> [DTC high, DTC low, status] …
        var codes = new List<ModuleDtc>();
        for (var i = 2; i + 2 < message.Length; i += 3)
        {
            var high = message[i];
            var low = message[i + 1];
            var status = message[i + 2];
            if (high == 0 && low == 0)
                continue;

            codes.Add(new ModuleDtc
            {
                Code = FormatCode(high, low),
                StatusByte = status,
                // KWP: bit 6 = currently present, bit 5 = stored.
                Active = (status & 0x40) != 0,
                Confirmed = (status & 0x20) != 0 || (status & 0x40) == 0,
                Pending = false
            });
        }

        return Distinct(codes);
    }

    internal static List<ModuleDtc> ParseGmlan(IReadOnlyList<byte[]> frames)
    {
        // Each frame: 81 <DTC high> <DTC low> <failure type> <status> …; 00 00 ends the list.
        var codes = new List<ModuleDtc>();
        foreach (var frame in frames)
        {
            if (frame.Length < 5 || frame[0] != 0x81)
                continue;

            var high = frame[1];
            var low = frame[2];
            if (high == 0 && low == 0)
                continue;

            var status = frame[4];
            codes.Add(new ModuleDtc
            {
                Code = FormatCode(high, low),
                FailureType = frame[3],
                StatusByte = status,
                Active = (status & 0x01) != 0 || (status & 0x02) != 0,
                Confirmed = (status & 0x08) != 0 || (status & 0x01) == 0,
                Pending = false
            });
        }

        return Distinct(codes);
    }

    internal static string FormatCode(byte high, byte low)
    {
        var letter = (high >> 6) switch { 0 => 'P', 1 => 'C', 2 => 'B', _ => 'U' };
        return $"{letter}{(high >> 4) & 0x03}{high & 0x0F:X}{low >> 4:X}{low & 0x0F:X}";
    }

    private static List<ModuleDtc> Distinct(List<ModuleDtc> codes) =>
        codes
            .GroupBy(code => (code.Code, code.FailureType))
            .Select(group => group.First())
            .ToList();

    // ------------------------------------------------------------------
    // Descriptions from the offline catalog
    // ------------------------------------------------------------------

    private async Task DescribeAsync(List<ModuleDtcReport> modules, CancellationToken token)
    {
        foreach (var code in modules.SelectMany(module => module.Codes))
        {
            try
            {
                var entry = await _catalog.GetAsync(code.Code, token).ConfigureAwait(false);
                if (entry is null)
                    continue;

                code.TitleEn = FirstNonEmpty(entry.TitleEn, entry.DescriptionEn);
                code.TitleKa = FirstNonEmpty(entry.TitleKa, entry.DescriptionKa);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    // ------------------------------------------------------------------
    // Candidates
    // ------------------------------------------------------------------

    private static void AddStandard(List<Candidate> candidates, bool is29BitCar)
    {
        if (is29BitCar)
        {
            // ISO 15765-4 29-bit physical addressing: engine 0x10, transmission 0x18.
            candidates.Add(Make("ECM", 0x18DA10F1, 0x18DAF110, true, 0));
            candidates.Add(Make("TCM", 0x18DA18F1, 0x18DAF118, true, 1));
            return;
        }

        foreach (var (tx, rx, id) in StandardObd)
            candidates.Add(Make(id, tx, rx, false, id == "ECM" ? 0 : id == "TCM" ? 1 : 30));
    }

    private static Candidate Make(string id, int tx, int rx, bool ext29, int priority)
    {
        var name = ModuleNames.FirstOrDefault(item => item.Id == id);
        if (name.Id is null)
            name = ("MOD", "მოდული", "Module");

        var suffix = id is "MOD" or "OBD"
            ? ext29 ? $" 0x{tx:X8}" : $" 0x{tx:X3}"
            : string.Empty;
        if (tx == 0x6F1 && id is "MOD")
            suffix = $" 0x{rx - 0x600:X2}";
        return new Candidate(id, name.Ka + suffix, name.En + suffix, tx, rx, ext29, priority);
    }

    private static int PriorityOf(string id) => id switch
    {
        "ECM" => 0,
        "TCM" => 1,
        "ABS" => 2,
        "SRS" => 3,
        "EPS" => 4,
        "BCM" => 5,
        "IPC" => 6,
        "HYB" or "BMS" => 7,
        "HVAC" => 8,
        "GW" => 9,
        "KEY" => 10,
        "ADAS" or "PARK" => 11,
        "RADIO" => 12,
        _ => 20
    };

    private sealed class AddressRow
    {
        public string? EcuType { get; set; }
        public long? RequestAddress { get; set; }
        public long? ResponseAddress { get; set; }
    }

    private async Task<List<Candidate>> LoadCatalogCandidatesAsync(BrandFamily family, CancellationToken token)
    {
        var makes = CatalogMakes(family);
        var result = new List<Candidate>();
        if (makes.Length == 0)
            return result;

        try
        {
            await _catalog.EnsureLoadedAsync(token).ConfigureAwait(false);
            var connection = new SQLiteAsyncConnection(_catalog.DatabasePath, SQLiteOpenFlags.ReadOnly);
            try
            {
                var placeholders = string.Join(",", makes.Select(_ => "?"));
                var rows = await connection.QueryAsync<AddressRow>(
                    $"""
                    SELECT ecu_type AS EcuType,
                           request_address AS RequestAddress,
                           response_address AS ResponseAddress
                    FROM ecu_addresses
                    WHERE upper(manufacturer) IN ({placeholders})
                      AND request_address IS NOT NULL
                      AND sub_address IS NULL
                      AND lin_address IS NULL;
                    """,
                    makes.Cast<object>().ToArray()).ConfigureAwait(false);

                foreach (var row in rows)
                {
                    var tx = (int)(row.RequestAddress ?? 0);
                    if (tx <= 0)
                        continue;

                    var ext29 = tx > 0x7FF;
                    if (!ext29 && tx is < 0x600 && family != BrandFamily.Gm)
                    {
                        // Low 11-bit IDs are normal traffic on most cars: skip.
                        continue;
                    }

                    int rx;
                    if (row.ResponseAddress is > 0)
                        rx = (int)row.ResponseAddress.Value;
                    else if (ext29 && (tx & 0x1FFF0000) == 0x18DA0000)
                        rx = (int)(0x18DA0000 | ((tx & 0xFF) << 8) | ((tx >> 8) & 0xFF));
                    else if (!ext29)
                        rx = tx + 8;
                    else
                        continue;

                    if (rx > (ext29 ? 0x1FFFFFFF : 0x7FF))
                        continue;

                    var id = MapCatalogType(row.EcuType);
                    result.Add(Make(id, tx, rx, ext29, PriorityOf(id) + 1));
                }
            }
            finally
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Module address catalog failed: {ex.Message}");
        }

        return result;
    }

    private static string MapCatalogType(string? type)
    {
        var value = (type ?? string.Empty).ToLowerInvariant();
        if (value.Contains("abs") || value.Contains("vsa") || value.Contains("brake")) return "ABS";
        if (value.Contains("srs") || value.Contains("airbag")) return "SRS";
        if (value.Contains("eps") || value.Contains("steer")) return "EPS";
        if (value.Contains("bcm") || value.Contains("body")) return "BCM";
        if (value.Contains("cluster") || value.Contains("combinationmeter") || value.Contains("hud")) return "IPC";
        if (value.Contains("hvac")) return "HVAC";
        if (value.Contains("transmission") || value.Contains("tcm") || value.Contains("shift")) return "TCM";
        if (value.Contains("engine") || value.Contains("ecm")) return "ECM";
        if (value.Contains("bms") || value.Contains("battery")) return "BMS";
        if (value.Contains("hybrid") || value.Contains("vcu")) return "HYB";
        if (value.Contains("gateway")) return "GW";
        if (value.Contains("radar") || value.Contains("camera") || value.Contains("adas") || value.Contains("dsu")) return "ADAS";
        if (value.Contains("infotainment") || value.Contains("telematics") || value.Contains("navigation")) return "RADIO";
        return "MOD";
    }

    private static string[] CatalogMakes(BrandFamily family) => family switch
    {
        BrandFamily.Gm => new[] { "CHEVROLET", "CADILLAC" },
        BrandFamily.Ford => new[] { "FORD" },
        BrandFamily.Mazda => new[] { "MAZDA" },
        BrandFamily.HyundaiKia => new[] { "HYUNDAI", "KIA", "HYUNDAI/KIA" },
        BrandFamily.Toyota => new[] { "TOYOTA" },
        BrandFamily.Honda => new[] { "HONDA" },
        BrandFamily.VwGroup => new[] { "VOLKSWAGEN" },
        BrandFamily.Nissan => new[] { "NISSAN" },
        BrandFamily.Subaru => new[] { "SUBARU" },
        BrandFamily.Stellantis => new[] { "PSA/STELLANTIS", "FIAT" },
        BrandFamily.Chrysler => new[] { "CHRYSLER" },
        BrandFamily.Renault => new[] { "RENAULT" },
        BrandFamily.Bmw => new[] { "BMW", "MINI" },
        _ => Array.Empty<string>()
    };

    private static BrandFamily DetectFamily(VehicleInfo vehicle)
    {
        var make = (vehicle.Manufacturer ?? string.Empty).ToUpperInvariant();
        bool Has(params string[] names) => names.Any(name => make.Contains(name, StringComparison.Ordinal));

        if (Has("CHEVROLET", "GMC", "BUICK", "CADILLAC", "GENERAL MOTORS", "HOLDEN", "PONTIAC", "SATURN", "HUMMER")) return BrandFamily.Gm;
        if (Has("FORD", "LINCOLN", "MERCURY")) return BrandFamily.Ford;
        if (Has("MAZDA")) return BrandFamily.Mazda;
        if (Has("HYUNDAI", "KIA", "GENESIS")) return BrandFamily.HyundaiKia;
        if (Has("TOYOTA", "LEXUS", "SCION")) return BrandFamily.Toyota;
        if (Has("HONDA", "ACURA")) return BrandFamily.Honda;
        if (Has("VOLKSWAGEN", "AUDI", "SKODA", "ŠKODA", "SEAT", "CUPRA", "PORSCHE")) return BrandFamily.VwGroup;
        if (Has("NISSAN", "INFINITI")) return BrandFamily.Nissan;
        if (Has("SUBARU")) return BrandFamily.Subaru;
        if (Has("PEUGEOT", "CITROEN", "CITROËN", "DS ", "OPEL", "VAUXHALL", "FIAT", "ALFA", "STELLANTIS")) return BrandFamily.Stellantis;
        if (Has("CHRYSLER", "DODGE", "JEEP", "RAM")) return BrandFamily.Chrysler;
        if (Has("RENAULT", "DACIA")) return BrandFamily.Renault;
        if (Has("BMW", "MINI")) return BrandFamily.Bmw;

        // VIN world manufacturer identifier as a fallback.
        var vin = (vehicle.VIN ?? string.Empty).Trim().ToUpperInvariant();
        if (vin.Length >= 3)
        {
            var wmi = vin[..3];
            if (wmi is "1G1" or "1GC" or "1GT" or "1G4" or "1G6" or "2G1" or "3G1" or "1GN" or "1GK" or "KL1" or "KL8") return BrandFamily.Gm;
            if (wmi.StartsWith("1F", StringComparison.Ordinal) || wmi.StartsWith("3F", StringComparison.Ordinal) || wmi == "WF0") return BrandFamily.Ford;
            if (wmi.StartsWith("KM", StringComparison.Ordinal) || wmi.StartsWith("KN", StringComparison.Ordinal) || wmi is "5NP" or "5XY" or "5NM") return BrandFamily.HyundaiKia;
            if (wmi.StartsWith("JT", StringComparison.Ordinal) || wmi is "4T1" or "4T3" or "5TD" or "5TF" or "2T1") return BrandFamily.Toyota;
            if (wmi is "JHM" or "1HG" or "2HG" or "5FN" or "5J6" or "19X" or "JH4") return BrandFamily.Honda;
            if (wmi is "WVW" or "WVG" or "WAU" or "TMB" or "VSS" or "3VW" or "1VW" or "WP0") return BrandFamily.VwGroup;
            if (wmi.StartsWith("JN", StringComparison.Ordinal) || wmi is "1N4" or "1N6" or "3N1" or "5N1") return BrandFamily.Nissan;
            if (wmi.StartsWith("JM", StringComparison.Ordinal)) return BrandFamily.Mazda;
            if (wmi.StartsWith("JF", StringComparison.Ordinal) || wmi == "4S3" || wmi == "4S4") return BrandFamily.Subaru;
            if (wmi is "VF3" or "VF7" or "VR3" or "W0L" or "ZFA") return BrandFamily.Stellantis;
            if (wmi is "1C3" or "1C4" or "1C6" or "2C3" or "3C4" or "2C4" or "1J4") return BrandFamily.Chrysler;
            if (wmi is "VF1" or "UU1") return BrandFamily.Renault;
            if (wmi is "WBA" or "WBS" or "WMW") return BrandFamily.Bmw;
        }

        return string.IsNullOrWhiteSpace(make) || make == "UNKNOWN" ? BrandFamily.Unknown : BrandFamily.Other;
    }
}
