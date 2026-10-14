using System.Globalization;
using System.Text.Json;
using CarScanner.Models;
using CarScanner.Services.Localization;
using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;
using CarScanner.Services.UDS;

namespace CarScanner.Services.Coding;

/// <summary>
/// Safe coding: full read-only backups of every module, byte/bit comparison
/// of two backups, and writes ONLY for catalog options verified on the same
/// car (brand, model, year, module address, part number). Every write is
/// preceded by a fresh backup and followed by a read-back; a mismatch is
/// restored automatically.
/// </summary>
public sealed class CodingService
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromMilliseconds(2500);

    /// <summary>Standard UDS identification identifiers (read-only).</summary>
    private static readonly int[] StandardDids =
    {
        0xF187, 0xF188, 0xF189, 0xF18A, 0xF18C, 0xF190, 0xF191, 0xF192,
        0xF193, 0xF194, 0xF195, 0xF197, 0xF19E
    };

    /// <summary>GMLAN $1A identifiers (GM modules, read-only).</summary>
    private static readonly byte[] GmlanDids =
    {
        0x90, 0x99, 0xB0, 0xB4, 0xC0, 0xC1, 0xC2, 0xC3, 0xC4, 0xCB, 0xCC
    };

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly OBDService _obd;
    private readonly ModuleDtcScanService _modules;
    private readonly VehicleInfoService _vehicle;
    private readonly LiveDataEngine _engine;
    private readonly LocalizationService _localization;
    private readonly VehicleBrandProfileService _brandProfiles;
    private readonly FreezeFrameReaderService _freezeFrame;
    private readonly CarScanner.Services.OemDiagnostics.ServiceDataReaderService _serviceData;
    private readonly AdvancedDiagnosticService _advancedDiagnostic;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly IsoTpReassembler _isoTp = new();

    private CatalogFile? _catalog;

    public CodingService(
        OBDService obd,
        ModuleDtcScanService modules,
        VehicleInfoService vehicle,
        LiveDataEngine engine,
        LocalizationService localization,
        VehicleBrandProfileService brandProfiles,
        FreezeFrameReaderService freezeFrame,
        CarScanner.Services.OemDiagnostics.ServiceDataReaderService serviceData,
        AdvancedDiagnosticService advancedDiagnostic)
    {
        _brandProfiles = brandProfiles;
        _obd = obd;
        _modules = modules;
        _vehicle = vehicle;
        _engine = engine;
        _localization = localization;
        _freezeFrame = freezeFrame;
        _serviceData = serviceData;
        _advancedDiagnostic = advancedDiagnostic;
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    public bool IsConnected => _obd.IsInitialized;

    public VehicleInfo? Vehicle => _vehicle.CurrentVehicle;

    public string VehicleName
    {
        get
        {
            var car = _vehicle.CurrentVehicle;
            if (car is null) return L("მანქანა არ არის დაკავშირებული", "No vehicle connected");
            var name = string.Join(" ", new[] { car.Manufacturer, car.Model, car.Year }
                .Where(p => !string.IsNullOrWhiteSpace(p) && !p.Equals("Unknown", StringComparison.OrdinalIgnoreCase)));
            return string.IsNullOrWhiteSpace(name) ? L("უცნობი მანქანა", "Unknown vehicle") : name;
        }
    }

    // ------------------------------------------------------------------
    // Catalog
    // ------------------------------------------------------------------

    private sealed class CatalogFile
    {
        public int SchemaVersion { get; set; }
        public int CatalogVersion { get; set; }
        public Dictionary<string, List<string>> BrandReadDids { get; set; } = new();
        public List<CodingOption> Options { get; set; } = new();
    }

    private async Task<CatalogFile> CatalogAsync()
    {
        if (_catalog is not null)
            return _catalog;
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("coding_catalog.json").ConfigureAwait(false);
            _catalog = await JsonSerializer.DeserializeAsync<CatalogFile>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).ConfigureAwait(false) ?? new CatalogFile();
        }
        catch
        {
            _catalog = new CatalogFile();
        }
        return _catalog;
    }

    private static string BrandKey(VehicleInfo? car)
    {
        var make = (car?.Manufacturer ?? string.Empty).ToUpperInvariant();
        if (make.Contains("VOLKSWAGEN") || make == "VW") return "VW";
        if (make.Contains("AUDI")) return "AUDI";
        if (make.Contains("SKODA") || make.Contains("ŠKODA")) return "SKODA";
        if (make.Contains("SEAT") || make.Contains("CUPRA")) return "SEAT";
        if (make.Contains("LINCOLN")) return "LINCOLN";
        if (make.Contains("FORD")) return "FORD";
        if (make.Contains("LEXUS")) return "LEXUS";
        if (make.Contains("TOYOTA")) return "TOYOTA";
        if (make.Contains("CHEVROLET") || make.Contains("GMC") || make.Contains("CADILLAC") || make.Contains("BUICK") || make.Contains("OPEL")) return "GM";
        return make.Trim();
    }

    /// <summary>Tells one car from another (VIN, else make/model/year); empty when nothing identifies the car.</summary>
    public string CurrentVehicleKey()
    {
        var car = _vehicle.CurrentVehicle;
        return CodingCapabilities.VehicleKey(car?.VIN, car?.Manufacturer, car?.Model, car?.Year);
    }

    /// <summary>How many verified settings the catalog lists in total (for every vehicle).</summary>
    public async Task<int> VerifiedOptionCountInCatalogAsync()
    {
        var catalog = await CatalogAsync().ConfigureAwait(false);
        return catalog.Options.Count(o => string.Equals(o.Status, "verified", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Changes made by this app that can still be put back.</summary>
    public int RestorableChangeCount() => History().Count(change => !change.Restored);

    public bool IsCanConnection => _obd.IsInitialized && _obd.IsCanProtocol;

    /// <summary>The protocol the adapter reported, e.g. "ISO 15765-4 (CAN 11/500)"; empty when not connected.</summary>
    public string ProtocolText => _obd.IsInitialized ? _obd.CurrentProtocolDescription : string.Empty;

    /// <summary>
    /// The facts for the readiness list. Only read-only requests are sent (speed and battery voltage, as the write
    /// path would check them); nothing is sent when the adapter is not connected or the connection is not CAN.
    /// </summary>
    public async Task<CodingReadinessInput> ReadReadinessAsync(CancellationToken token)
    {
        var connected = _obd.IsInitialized;
        var isCan = IsCanConnection;
        var car = _vehicle.CurrentVehicle;
        var identified = car is not null &&
                         !string.IsNullOrWhiteSpace(car.Manufacturer) && !car.Manufacturer.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(car.Model) && !car.Model.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

        double? speed = null;
        double? volts = null;
        if (connected && isCan)
        {
            speed = await ReadPidAsync(0x0D, token).ConfigureAwait(false);
            volts = await ReadVoltageAsync(token).ConfigureAwait(false);
        }

        return new CodingReadinessInput(connected, isCan, car?.VIN, speed, volts, identified);
    }

    /// <summary>Verified options that match the connected car (brand/model/year).</summary>
    public async Task<IReadOnlyList<CodingOption>> OptionsForCurrentCarAsync()
    {
        var catalog = await CatalogAsync().ConfigureAwait(false);
        var car = _vehicle.CurrentVehicle;
        if (car is null)
            return Array.Empty<CodingOption>();

        var brand = BrandKey(car);
        int.TryParse(car.Year, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year);
        var model = (car.Model ?? string.Empty).ToUpperInvariant();

        return catalog.Options
            .Where(o => string.Equals(o.Status, "verified", StringComparison.OrdinalIgnoreCase))
            .Where(o => string.Equals(o.Brand, brand, StringComparison.OrdinalIgnoreCase))
            .Where(o => o.Models.Count == 0 || o.Models.Any(m => model.Contains(m.ToUpperInvariant())))
            .Where(o => year == 0 ? false : year >= o.YearFrom && year <= o.YearTo)
            .Where(o => o.TxId > 0 && o.RxId > 0 && o.DidId >= 0 && o.Values.Count >= 2)
            // Without a module part-number list the target module cannot be
            // identified before writing: such an option is not verified.
            .Where(o => o.PartNumbers.Any(p => !string.IsNullOrWhiteSpace(p)))
            .ToList();
    }

    // ------------------------------------------------------------------
    // Storage
    // ------------------------------------------------------------------

    private static string Root
    {
        get
        {
            var folder = Path.Combine(FileSystem.AppDataDirectory, "coding");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    private string VinKey => _vehicle.CurrentVehicle?.VIN is { Length: 17 } vin ? vin.ToUpperInvariant() : "unknown";

    private string BackupFolder(string? vinKey = null)
    {
        var folder = Path.Combine(Root, vinKey ?? VinKey, "backups");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private string HistoryPath => Path.Combine(Root, VinKey, "history.json");

    public string BackupPath(CodingBackup backup) => Path.Combine(BackupFolder(KeyOf(backup.Vin)), $"{backup.Id}.json");

    private static string KeyOf(string vin) => vin is { Length: 17 } ? vin.ToUpperInvariant() : "unknown";

    public IReadOnlyList<CodingBackup> ListBackups()
    {
        var list = new List<CodingBackup>();
        foreach (var file in Directory.EnumerateFiles(BackupFolder(), "*.json"))
        {
            try
            {
                var backup = JsonSerializer.Deserialize<CodingBackup>(File.ReadAllText(file));
                if (backup is not null) list.Add(backup);
            }
            catch
            {
                // A damaged file must not hide the other backups.
            }
        }
        return list.OrderByDescending(b => b.CreatedAt).ToList();
    }

    private void SaveBackup(CodingBackup backup) =>
        File.WriteAllText(BackupPath(backup), JsonSerializer.Serialize(backup, Json));

    public void DeleteBackup(CodingBackup backup)
    {
        var path = BackupPath(backup);
        if (File.Exists(path)) File.Delete(path);
    }

    public IReadOnlyList<CodingChange> History()
    {
        try
        {
            return File.Exists(HistoryPath)
                ? JsonSerializer.Deserialize<List<CodingChange>>(File.ReadAllText(HistoryPath)) ?? new List<CodingChange>()
                : new List<CodingChange>();
        }
        catch
        {
            return new List<CodingChange>();
        }
    }

    private void SaveHistory(IEnumerable<CodingChange> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(items.OrderByDescending(c => c.At).Take(200).ToList(), Json));
    }

    // ------------------------------------------------------------------
    // Backup (read-only)
    // ------------------------------------------------------------------

    /// <summary>True for GM-family cars (Chevrolet, GMC, Cadillac, Buick, Opel): GMLAN modules, no configuration identifiers in the catalog.</summary>
    public bool IsGmVehicle
    {
        get
        {
            var car = _vehicle.CurrentVehicle;
            if (BrandKey(car) == "GM")
                return true;

            // Without an identified make (no VIN) the brand the person chose before connecting is a hint — used only to
            // OFFER the read-only extended read. It is never used to match a setting: that needs the identified vehicle.
            var identified = !string.IsNullOrWhiteSpace(car?.Manufacturer) &&
                             !car!.Manufacturer.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
            return !identified && BrandKey(new VehicleInfo { Manufacturer = _brandProfiles.Current.DisplayName }) == "GM";
        }
    }

    /// <param name="extendedGmRead">
    /// GM only: after the normal identification reads, sweeps the GMLAN $1A identifiers 00–FF of every GMLAN module that
    /// answered (read-only), so that a before/after comparison can show where a setting lives.
    /// </param>
    /// <param name="deepScan">
    /// Admin-only Deep Scan. On top of <paramref name="extendedGmRead"/> (unchanged, GM/$1A only), this also: sweeps
    /// the UDS $22 identifier space of every answering module regardless of brand/protocol (<see cref="UdsDidSweep"/>);
    /// reads the service/maintenance values (oil-life % etc.) already exposed by the Service page's own infrastructure;
    /// reads the stored freeze frame (Mode 02); and reads the Mode 09 identifiers beyond VIN (calibration ID, CVN, ECU
    /// name). All of it read-only. The freeze-frame and Mode 09 values are not in ObdExchangeTap's observable set
    /// (Mode 09 deliberately, since it would include the VIN) so they stay local to this backup only.
    /// </param>
    public async Task<CodingBackup> CreateBackupAsync(string note, IProgress<(double Fraction, string Text)>? progress, CancellationToken token, bool extendedGmRead = false, bool deepScan = false)
    {
        if (!_obd.IsInitialized)
            throw new InvalidOperationException(L("ჯერ OBD ადაპტერს დაუკავშირდით.", "Connect to the OBD adapter first."));

        await _busy.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // The module list of an earlier scan is reused only for the SAME car: after switching cars it would point at
            // addresses that do not belong to this one and file their answers under the wrong vehicle.
            var vehicleKey = CurrentVehicleKey();
            var modules = vehicleKey.Length > 0 && string.Equals(_modules.LastScanVehicleKey, vehicleKey, StringComparison.Ordinal)
                ? _modules.LastModules.Where(m => m.Responded).ToList()
                : new List<ModuleDtcReport>();
            if (modules.Count == 0)
            {
                var car = _vehicle.CurrentVehicle ?? new VehicleInfo();
                var speed = await ReadPidAsync(0x0D, token).ConfigureAwait(false);
                var outcome = await _modules.ScanAsync(car, speed,
                    new Progress<(double Fraction, string Text)>(p => progress?.Report((p.Fraction * 0.4, p.Text))),
                    token).ConfigureAwait(false);
                modules = outcome.Modules.Where(m => m.Responded).ToList();
            }

            // Some adapters/cars (e.g. Chevrolet Volt) fail the full module scan.
            // Ask the standard engine-bay addresses directly with read-only requests.
            if (modules.Count == 0)
                modules = await ProbeStandardModulesAsync(progress, token).ConfigureAwait(false);

            if (modules.Count == 0)
                throw new InvalidOperationException(L(
                    "მოდულებმა არ უპასუხეს. ჩართე მანქანა (ანთება ან READY) და სცადე თავიდან.",
                    "No module answered. Turn the car on (ignition or READY) and try again."));

            var catalog = await CatalogAsync().ConfigureAwait(false);
            var brandDids = catalog.BrandReadDids.TryGetValue(BrandKey(_vehicle.CurrentVehicle), out var extra)
                ? extra.Select(d => int.TryParse(d, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : -1).Where(v => v >= 0).ToList()
                : new List<int>();

            var backup = new CodingBackup
            {
                Vin = _vehicle.CurrentVehicle?.VIN ?? string.Empty,
                Vehicle = VehicleName,
                Note = note ?? string.Empty
            };

            for (var i = 0; i < modules.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var module = modules[i];
                var name = _localization.IsEnglish ? module.NameEn : module.NameKa;
                progress?.Report((0.4 + 0.6 * i / modules.Count, L($"ვკითხულობ: {name}", $"Reading: {name}")));

                var item = new CodingModuleBackup
                {
                    ModuleId = module.ModuleId,
                    Name = string.IsNullOrWhiteSpace(module.EcuName) ? name : $"{name} · {module.EcuName}",
                    Tx = module.TransmitCanId,
                    Rx = module.ReceiveCanId,
                    Extended29Bit = module.Extended29Bit,
                    ExtendedAddress = module.ExtendedAddress,
                    SoftwareVersion = module.SoftwareVersion
                };

                if (string.Equals(module.Protocol, "GMLAN", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var did in GmlanDids)
                    {
                        var read = await ReadGmlanAsync(module, did, token).ConfigureAwait(false);
                        item.Values.Add(read);
                        // NRC 11 = service not supported: the remaining requests would only repeat the refusal.
                        if (read.Error == "NRC 11") break;
                    }
                }
                else
                {
                    foreach (var did in StandardDids.Concat(brandDids).Distinct())
                    {
                        var read = await ReadDidAsync(AddressOf(module), did, token).ConfigureAwait(false);
                        item.Values.Add(read);
                        if (read.Error == "NRC 11") break;
                    }
                }

                var part = item.Values.FirstOrDefault(v => v.Did is "F187" or "1A:CB" && v.IsRead);
                item.PartNumber = part is null ? string.Empty : AsciiOrHex(part.Hex);
                (item.Status, item.Note) = CodingBackupStatus.Classify(item.Values);

                if (extendedGmRead && item.Status == CodingBackupStatus.Read &&
                    string.Equals(module.Protocol, "GMLAN", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report((0.4 + 0.6 * i / modules.Count, L($"ვკითხულობ ყველა $1A იდენტიფიკატორს: {name}", $"Reading every $1A identifier: {name}")));
                    var sweep = await GmlanSweep.RunAsync(
                        did => ReadGmlanAsync(module, did, token),
                        GmlanDids.ToHashSet(),
                        progress: null,
                        token).ConfigureAwait(false);
                    item.Values.AddRange(sweep.Values);
                    item.Note = L($"გაფართოებული წაკითხვა: {sweep.Values.Count} ახალი მნიშვნელობა {sweep.Requests} მოთხოვნიდან",
                        $"Extended read: {sweep.Values.Count} more value(s) from {sweep.Requests} requests") +
                        (string.IsNullOrEmpty(sweep.Stopped) ? string.Empty : L($" · შეჩერდა: {sweep.Stopped}", $" · stopped: {sweep.Stopped}"));
                }

                // Deep Scan only: the $22 space, on every answering module regardless of brand/protocol (unlike the
                // $1A sweep above, which is GM-specific). A module already identified via $22 (every non-GMLAN
                // module) skips the DIDs it already has; a GMLAN module has none yet, since it was read via $1A.
                if (deepScan && item.Status == CodingBackupStatus.Read)
                {
                    progress?.Report((0.4 + 0.6 * i / modules.Count, L($"ვკითხულობ ყველა $22 იდენტიფიკატორს: {name}", $"Reading every $22 identifier: {name}")));
                    var alreadyRead22 = string.Equals(module.Protocol, "GMLAN", StringComparison.OrdinalIgnoreCase)
                        ? new HashSet<int>()
                        : StandardDids.Concat(brandDids).Distinct().ToHashSet();
                    var address = AddressOf(module);
                    var sweep22 = await UdsDidSweep.RunAsync(
                        did => ReadDidAsync(address, did, token),
                        alreadyRead22,
                        progress: null,
                        token).ConfigureAwait(false);
                    item.Values.AddRange(sweep22.Values);
                    var sweep22Note = L($"$22 გაფართოებული წაკითხვა: {sweep22.Values.Count} ახალი მნიშვნელობა {sweep22.Requests} მოთხოვნიდან",
                        $"$22 extended read: {sweep22.Values.Count} more value(s) from {sweep22.Requests} requests") +
                        (string.IsNullOrEmpty(sweep22.Stopped) ? string.Empty : L($" · შეჩერდა: {sweep22.Stopped}", $" · stopped: {sweep22.Stopped}"));
                    item.Note = string.IsNullOrEmpty(item.Note) ? sweep22Note : item.Note + " · " + sweep22Note;
                }

                backup.Modules.Add(item);
            }

            foreach (var module in backup.Modules.Where(m => m.Status != CodingBackupStatus.Read))
            {
                backup.Warnings.Add(module.Status switch
                {
                    CodingBackupStatus.Foreign => L($"{module.Name}: პასუხი სხვა მოდულიდან მოვიდა ({module.Note}) — მნიშვნელობები არ შენახულა",
                        $"{module.Name}: the reply came from another module ({module.Note}) — no values were saved"),
                    CodingBackupStatus.Refused => L($"{module.Name}: მოდულმა წაკითხვაზე უარი თქვა ({module.Note})",
                        $"{module.Name}: the module refused the read ({module.Note})"),
                    _ => L($"{module.Name}: პასუხი არ დააბრუნა", $"{module.Name}: did not answer")
                });
            }

            // Deep Scan only: vehicle-level reads that are not per-module DID lookups, so they live outside the
            // module loop above. Each is independently best-effort — one failing must never lose what the module
            // loop already found, so every read here is wrapped on its own.
            if (deepScan)
            {
                progress?.Report((0.95, L("ვკითხულობ სერვისისა და დამატებით მონაცემებს", "Reading service and extra data")));

                try
                {
                    var vehicleInfo = _vehicle.CurrentVehicle ?? new VehicleInfo();
                    var serviceRead = await _serviceData.ReadResearchAsync(vehicleInfo, token).ConfigureAwait(false);
                    foreach (var reading in serviceRead.Values)
                    {
                        backup.ServiceReadings.Add(new CodingDidValue
                        {
                            Did = reading.Kind.ToString(),
                            Hex = $"{reading.NumericValue:0.##} {reading.Unit}".Trim()
                        });
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { /* best effort: the rest of Deep Scan's result is still saved */ }

                try
                {
                    var frame = await _freezeFrame.ReadAsync(progress: null, token).ConfigureAwait(false);
                    if (frame.HasFrame)
                    {
                        backup.FreezeFrameTrigger = frame.TriggerCode;
                        foreach (var value in frame.Values)
                        {
                            backup.FreezeFrameValues.Add(new CodingDidValue
                            {
                                Did = $"02:{value.Pid:X2}",
                                Hex = value.Text ?? (value.Number is { } number ? $"{number:0.##} {value.MetricUnit}".Trim() : string.Empty)
                            });
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { }

                try
                {
                    // includeVin: false — the VIN is already known separately (backup.Vin); it must never be
                    // duplicated into this generic list, the same exclusion ObdExchangeTap itself applies to Mode 09.
                    var identifiers = await _advancedDiagnostic.ReadIdentifiersAsync(includeVin: false, token: token).ConfigureAwait(false);
                    foreach (var identifier in identifiers.Where(item => item.Available))
                        backup.Mode09Values.Add(new CodingDidValue { Did = identifier.Name, Hex = identifier.Value });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { }
            }

            // A "backup" with nothing in it would only look like one.
            if (backup.ReadCount == 0)
                throw new InvalidOperationException(L(
                    "ვერც ერთი მოდულიდან მნიშვნელობა ვერ წავიკითხე, ამიტომ ასლი არ შეინახა. " + string.Join(" ", backup.Warnings),
                    "No value could be read from any module, so no backup was saved. " + string.Join(" ", backup.Warnings)));

            progress?.Report((1, L("სარეზერვო ასლი შენახულია", "Backup saved")));
            SaveBackup(backup);
            return backup;
        }
        finally
        {
            _busy.Release();
        }
    }

    private static readonly (int Tx, string Ka, string En)[] StandardModuleNames =
    {
        (0x7E0, "ძრავი", "Engine"),
        (0x7E1, "ჰიბრიდის / კოლოფის მართვა", "Hybrid / transmission control"),
        (0x7E2, "გადაცემათა კოლოფი", "Transmission"),
        (0x7E3, "მოდული", "Module"),
        (0x7E4, "ენერგიის მოდული", "Energy module"),
        (0x7E5, "მოდული", "Module"),
        (0x7E6, "მოდული", "Module"),
        (0x7E7, "ბატარეის მართვა", "Battery control")
    };

    /// <summary>
    /// Fallback when the module scan finds nothing: asks 7E0–7E7 with read-only
    /// requests (UDS 22 F190, then GMLAN 1A 90). Any answer — even a refusal —
    /// means the module is there.
    /// </summary>
    private async Task<List<ModuleDtcReport>> ProbeStandardModulesAsync(
        IProgress<(double Fraction, string Text)>? progress, CancellationToken token)
    {
        var found = new List<ModuleDtcReport>();
        if (!_obd.IsCanProtocol)
            return found;

        for (var i = 0; i < StandardModuleNames.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var (tx, ka, en) = StandardModuleNames[i];
            var rx = tx + 8;
            progress?.Report((0.4 * i / StandardModuleNames.Length,
                L($"მოდულების ძებნა ({i + 1}/{StandardModuleNames.Length})", $"Looking for modules ({i + 1}/{StandardModuleNames.Length})")));

            var module = new ModuleDtcReport
            {
                ModuleId = $"ECU {tx:X3}",
                NameKa = ka,
                NameEn = en,
                TransmitCanId = tx,
                ReceiveCanId = rx,
                Extended29Bit = false
            };

            var uds = await ReadDidAsync(AddressOf(tx, rx, false), 0xF190, token).ConfigureAwait(false);
            if (uds.IsRead)
            {
                module.Protocol = "UDS";
                module.Responded = true;
                found.Add(module);
                continue;
            }

            // UDS refused or silent: GM cars answer the GMLAN way (1A).
            var gm = await ReadGmlanAsync(module, 0x90, token).ConfigureAwait(false);
            if (gm.IsRead || Answered(gm.Error) || Answered(uds.Error))
            {
                module.Protocol = gm.IsRead || Answered(gm.Error) ? "GMLAN" : "UDS";
                module.Responded = true;
                found.Add(module);
            }
        }

        return found;

        static bool Answered(string? error) =>
            !string.IsNullOrEmpty(error) && error.StartsWith("NRC", StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceDiagnosticAddress AddressOf(ModuleDtcReport m) =>
        new(m.TransmitCanId, m.ReceiveCanId, m.Extended29Bit, m.ExtendedAddress,
            m.Extended29Bit ? "29-bit CAN" : "11-bit CAN", $"TX 0x{m.TransmitCanId:X}");

    private static ServiceDiagnosticAddress AddressOf(int tx, int rx, bool ext29) =>
        new(tx, rx, ext29, null, ext29 ? "29-bit CAN" : "11-bit CAN", $"TX 0x{tx:X}");

    private async Task<CodingDidValue> ReadDidAsync(ServiceDiagnosticAddress address, int did, CancellationToken token)
    {
        var value = new CodingDidValue { Did = did.ToString("X4") };
        try
        {
            var raw = await _obd.ReadPhysicalCanReadOnlyAsync(address,
                new byte[] { 0x22, (byte)(did >> 8), (byte)did }, ReadTimeout, token).ConfigureAwait(false);
            var reply = ParseReply(raw, address.ReceiveCanId, 0x22, new[] { (byte)(did >> 8), (byte)did });
            value.Hex = reply.Data is { Length: > 0 } data ? Convert.ToHexString(data) : string.Empty;
            value.Error = reply.Data is null ? reply.Error : string.Empty;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            value.Error = ex is TimeoutException ? "timeout" : ex.GetType().Name;
        }
        return value;
    }

    private async Task<CodingDidValue> ReadGmlanAsync(ModuleDtcReport module, byte did, CancellationToken token)
    {
        var value = new CodingDidValue { Did = $"1A:{did:X2}" };
        if (module.Extended29Bit)
        {
            value.Error = "29-bit";
            return value;
        }
        try
        {
            var raw = await _obd.ReadGmlanDidFilteredAsync(module.TransmitCanId, module.ReceiveCanId, did, ReadTimeout, token).ConfigureAwait(false);
            var reply = ParseReply(raw, module.ReceiveCanId, 0x1A, new[] { did });
            value.Hex = reply.Data is { Length: > 0 } data ? Convert.ToHexString(data) : string.Empty;
            value.Error = reply.Data is null ? reply.Error : string.Empty;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            value.Error = ex is TimeoutException ? "timeout" : ex.GetType().Name;
        }
        return value;
    }

    /// <summary>
    /// Finds "service+0x40 id… data" (positive) or "7F service NRC" in an ELM327 reply; only frames of the asked module
    /// count (see <see cref="CodingReplyParser"/>).
    /// </summary>
    private static CodingReply ParseReply(string raw, int? rx, byte service, byte[] idBytes) =>
        CodingReplyParser.Parse(raw, rx, service, idBytes);

    private static string AsciiOrHex(string hex)
    {
        try
        {
            var bytes = Convert.FromHexString(hex);
            var text = new string(bytes.Select(b => b is >= 0x20 and < 0x7F ? (char)b : ' ').ToArray()).Trim();
            return text.Count(char.IsLetterOrDigit) >= 3 ? text : hex;
        }
        catch
        {
            return hex;
        }
    }

    // ------------------------------------------------------------------
    // Compare
    // ------------------------------------------------------------------

    public static IReadOnlyList<CodingDifference> Compare(CodingBackup before, CodingBackup after)
    {
        var result = new List<CodingDifference>();
        foreach (var moduleB in after.Modules)
        {
            var moduleA = before.Modules.FirstOrDefault(m => m.Tx == moduleB.Tx && m.Rx == moduleB.Rx);
            if (moduleA is null) continue;

            foreach (var valueB in moduleB.Values.Where(v => v.IsRead))
            {
                var valueA = moduleA.Values.FirstOrDefault(v => v.Did == valueB.Did && v.IsRead);
                if (valueA is null || valueA.Hex == valueB.Hex) continue;

                var a = Convert.FromHexString(valueA.Hex);
                var b = Convert.FromHexString(valueB.Hex);
                for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
                {
                    var x = i < a.Length ? a[i] : (byte?)null;
                    var y = i < b.Length ? b[i] : (byte?)null;
                    if (x == y) continue;

                    var bits = x.HasValue && y.HasValue
                        ? string.Join(",", Enumerable.Range(0, 8).Where(k => ((x.Value ^ y.Value) >> k & 1) == 1).Select(k => k.ToString(CultureInfo.InvariantCulture)))
                        : "—";
                    result.Add(new CodingDifference(
                        $"{moduleB.Name} (0x{moduleB.Tx:X})",
                        valueB.Did,
                        i,
                        x?.ToString("X2") ?? "—",
                        y?.ToString("X2") ?? "—",
                        bits));
                }
            }
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Safety checks
    // ------------------------------------------------------------------

    private async Task<double?> ReadPidAsync(int pid, CancellationToken token)
    {
        try
        {
            var value = await _obd.ReadSensorAsync(pid, token).ConfigureAwait(false);
            return value is not null && double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<double?> ReadVoltageAsync(CancellationToken token)
    {
        var pid = await ReadPidAsync(0x42, token).ConfigureAwait(false);
        if (pid is > 5) return pid;
        try
        {
            var raw = await _obd.ReadAdapterVoltageAsync(token).ConfigureAwait(false);
            var digits = new string((raw ?? string.Empty).Where(c => char.IsDigit(c) || c == '.').ToArray());
            return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 5 ? v : null;
        }
        catch
        {
            return null;
        }
    }

    public sealed record SafetyCheck(bool Ok, string Text);

    public async Task<IReadOnlyList<SafetyCheck>> CheckConditionsAsync(CodingOption option, CancellationToken token)
    {
        var checks = new List<SafetyCheck>();
        checks.Add(new SafetyCheck(_obd.IsInitialized, L("ადაპტერი დაკავშირებულია", "Adapter connected")));
        if (_engine.KeepRunningForTrip)
            checks.Add(new SafetyCheck(false, L("ჯერ დაასრულეთ მიმდინარე მოგზაურობა (Fuel გვერდი)", "Finish the running trip first (Fuel page)")));

        var speed = await ReadPidAsync(0x0D, token).ConfigureAwait(false);
        checks.Add(new SafetyCheck(speed is not null && speed < 0.5,
            speed is null ? L("სიჩქარე ვერ წავიკითხე", "Speed could not be read") : L($"მანქანა დგას ({speed:0} კმ/სთ)", $"Car is stopped ({speed:0} km/h)")));

        if (option.RequiresEngineOff)
        {
            var rpm = await ReadPidAsync(0x0C, token).ConfigureAwait(false);
            // RPM unreadable with the engine off on many cars; that is acceptable.
            checks.Add(new SafetyCheck(rpm is null || rpm < 50,
                rpm is null or < 50 ? L("ძრავი გამორთულია (ანთება ჩართული)", "Engine off (ignition on)") : L($"ძრავი მუშაობს ({rpm:0} rpm) — გამორთეთ", $"Engine running ({rpm:0} rpm) — switch it off")));
        }

        var volts = await ReadVoltageAsync(token).ConfigureAwait(false);
        checks.Add(new SafetyCheck(volts is >= 12.2,
            volts is null ? L("ძაბვა ვერ წავიკითხე", "Voltage could not be read") : L($"აკუმულატორი {volts:0.0} V (მინ. 12.2 V)", $"Battery {volts:0.0} V (min 12.2 V)")));

        if (option.RequiresSecurityAccess)
            checks.Add(new SafetyCheck(false, L("ეს პარამეტრი დაცვის კოდს ითხოვს — ჯერ მხარდაჭერილი არ არის", "This setting needs security access — not supported yet")));

        if (_obd.CurrentProtocol is not (ObdProtocol.ISO_15765_4_CAN_11BIT_500K or ObdProtocol.ISO_15765_4_CAN_11BIT_250K
            or ObdProtocol.ISO_15765_4_CAN_29BIT_500K or ObdProtocol.ISO_15765_4_CAN_29BIT_250K))
            checks.Add(new SafetyCheck(false, L("კავშირი არ არის CAN", "Connection is not CAN")));

        return checks;
    }

    // ------------------------------------------------------------------
    // Read / write one option
    // ------------------------------------------------------------------

    public sealed record OptionState(byte[]? Block, CodingValueChoice? Current, string PartNumber, string Error);

    public async Task<OptionState> ReadOptionAsync(CodingOption option, CancellationToken token)
    {
        var ext29 = option.TxId > 0x7FF;
        var address = AddressOf(option.TxId, option.RxId, ext29);
        var block = await ReadDidAsync(address, option.DidId, token).ConfigureAwait(false);
        if (!block.IsRead)
            return new OptionState(null, null, string.Empty, block.Error);

        var part = await ReadDidAsync(address, 0xF187, token).ConfigureAwait(false);
        var partText = part.IsRead ? AsciiOrHex(part.Hex) : string.Empty;
        var bytes = Convert.FromHexString(block.Hex);
        if (option.ByteIndex >= bytes.Length)
            return new OptionState(bytes, null, partText, L("ბლოკი მოსალოდნელზე მოკლეა", "Block is shorter than expected"));

        var raw = (byte)(bytes[option.ByteIndex] & option.MaskByte);
        var current = option.Values.FirstOrDefault(v => ParseByte(v.Raw) is byte b && (b & option.MaskByte) == raw);
        return new OptionState(bytes, current, partText, current is null ? L("უცნობი მნიშვნელობა — ცვლილება დაბლოკილია", "Unknown value — change blocked") : string.Empty);
    }

    private static byte? ParseByte(string hex) =>
        byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) ? b : null;

    public async Task<CodingResult> WriteOptionAsync(CodingOption option, CodingValueChoice target, IProgress<string>? progress, CancellationToken token)
    {
        await _busy.WaitAsync(token).ConfigureAwait(false);
        var engineWasRunning = _engine.IsRunning;
        try
        {
            // 1. Option must still be a verified match for this car.
            if (!(await OptionsForCurrentCarAsync().ConfigureAwait(false)).Any(o => o.Id == option.Id))
                return new CodingResult(false, L("ეს პარამეტრი ამ მანქანისთვის დადასტურებული არ არის.", "This setting is not verified for this car."));

            var targetByte = ParseByte(target.Raw);
            if (targetByte is null || !option.Values.Contains(target))
                return new CodingResult(false, L("არასწორი მნიშვნელობა.", "Invalid value."));

            if (engineWasRunning)
                await _engine.StopAsync(force: true).ConfigureAwait(false);

            // 2. Conditions.
            progress?.Report(L("ვამოწმებ პირობებს…", "Checking conditions…"));
            var checks = await CheckConditionsAsync(option, token).ConfigureAwait(false);
            var failed = checks.FirstOrDefault(c => !c.Ok);
            if (failed is not null)
                return new CodingResult(false, failed.Text);

            // 3. Fresh read of the block + part number gate.
            progress?.Report(L("ვკითხულობ მიმდინარე მნიშვნელობას…", "Reading the current value…"));
            var state = await ReadOptionAsync(option, token).ConfigureAwait(false);
            if (state.Block is null || state.Current is null)
                return new CodingResult(false, string.IsNullOrWhiteSpace(state.Error) ? L("მნიშვნელობა ვერ წავიკითხე.", "Could not read the value.") : state.Error);
            // The module must identify itself with a listed part number; an
            // unreadable part number or an option without a list blocks the write.
            var modulePart = (state.PartNumber ?? string.Empty).Replace(" ", "");
            if (modulePart.Length == 0 ||
                !option.PartNumbers.Where(p => !string.IsNullOrWhiteSpace(p))
                    .Any(p => modulePart.Contains(p.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)))
                return new CodingResult(false, L($"მოდულის ნაწილის ნომერი ({state.PartNumber}) სიაში არ არის — ცვლილება დაბლოკილია.", $"Module part number ({state.PartNumber}) is not on the list — change blocked."));
            if (state.Current.Raw == target.Raw)
                return new CodingResult(true, L("უკვე ეს მნიშვნელობაა დაყენებული.", "This value is already set."));

            var original = state.Block;
            var updated = (byte[])original.Clone();
            updated[option.ByteIndex] = (byte)((original[option.ByteIndex] & ~option.MaskByte) | (targetByte.Value & option.MaskByte));
            if (3 + updated.Length > 7)
                return new CodingResult(false, L("ეს ბლოკი ძალიან გრძელია ამ ადაპტერისთვის (მაქს. 4 ბაიტი).", "This block is too long for this adapter (max 4 bytes)."));

            // 4. Full backup right before writing.
            progress?.Report(L("ვქმნი სარეზერვო ასლს…", "Creating a backup…"));
            _busy.Release();
            CodingBackup backup;
            try
            {
                backup = await CreateBackupAsync(L($"ცვლილებამდე: {option.TitleKa}", $"Before: {option.TitleEn}"), null, token).ConfigureAwait(false);
            }
            finally
            {
                await _busy.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            // 5. Write + read-back (+ automatic restore).
            var result = await WriteBlockAsync(option, original, updated, progress, token).ConfigureAwait(false);
            if (result.Success)
            {
                var history = History().ToList();
                history.Insert(0, new CodingChange
                {
                    Vin = backup.Vin,
                    OptionId = option.Id,
                    Title = L(option.TitleKa, option.TitleEn) + ": " + L(target.Ka, target.En),
                    Tx = option.TxId,
                    Rx = option.RxId,
                    Did = option.Did,
                    BeforeHex = Convert.ToHexString(original),
                    AfterHex = Convert.ToHexString(updated)
                });
                SaveHistory(history);
            }
            return result;
        }
        finally
        {
            _busy.Release();
            if (engineWasRunning)
            {
                try { await _engine.StartAsync().ConfigureAwait(false); } catch { }
            }
        }
    }

    /// <summary>Puts the "before" bytes of a logged change back.</summary>
    public async Task<CodingResult> RestoreAsync(CodingChange change, IProgress<string>? progress, CancellationToken token)
    {
        var option = (await CatalogAsync().ConfigureAwait(false)).Options.FirstOrDefault(o => o.Id == change.OptionId);
        if (option is null)
            return new CodingResult(false, L("პარამეტრი კატალოგში აღარ არის.", "The setting is no longer in the catalog."));

        await _busy.WaitAsync(token).ConfigureAwait(false);
        var engineWasRunning = _engine.IsRunning;
        try
        {
            if (engineWasRunning)
                await _engine.StopAsync(force: true).ConfigureAwait(false);

            var failed = (await CheckConditionsAsync(option, token).ConfigureAwait(false)).FirstOrDefault(c => !c.Ok);
            if (failed is not null)
                return new CodingResult(false, failed.Text);

            // The logged "before" bytes belong to one car: never write them to another.
            var changeVin = (change.Vin ?? string.Empty).Trim();
            if (changeVin.Length != 17)
                return new CodingResult(false, L(
                    "ამ ჩანაწერს VIN არ აქვს, ამიტომ ვერ დავადასტურებ, რომ იგივე მანქანაა — აღდგენა დაბლოკილია.",
                    "This record has no VIN, so it cannot be confirmed to be the same car — restore blocked."));

            var connectedVin = await ReadConnectedVinAsync(token).ConfigureAwait(false);
            if (connectedVin is null)
                return new CodingResult(false, L(
                    "დაკავშირებული მანქანის VIN ვერ წავიკითხე — აღდგენა დაბლოკილია. სცადე თავიდან.",
                    "Could not read the connected car's VIN — restore blocked. Try again."));
            if (!string.Equals(connectedVin, changeVin, StringComparison.OrdinalIgnoreCase))
                return new CodingResult(false, L(
                    $"ეს ცვლილება სხვა მანქანაზე (VIN {changeVin}) გაკეთდა, დაკავშირებულია {connectedVin} — აღდგენა დაბლოკილია.",
                    $"This change was made on another car (VIN {changeVin}); the connected car is {connectedVin} — restore blocked."));

            var address = AddressOf(change.Tx, change.Rx, change.Tx > 0x7FF);
            var current = await ReadDidAsync(address, option.DidId, token).ConfigureAwait(false);
            if (!current.IsRead)
                return new CodingResult(false, L("მიმდინარე მნიშვნელობა ვერ წავიკითხე.", "Could not read the current value."));
            if (current.Hex == change.BeforeHex)
            {
                MarkRestored(change);
                return new CodingResult(true, L("თავდაპირველი მნიშვნელობა უკვე დაყენებულია.", "The original value is already set."));
            }
            if (current.Hex != change.AfterHex)
                return new CodingResult(false, L("მნიშვნელობა მას შემდეგ სხვა ხელსაწყომ შეცვალა — აღდგენა დაბლოკილია.", "The value was changed by something else since — restore blocked."));

            var result = await WriteBlockAsync(option, Convert.FromHexString(change.AfterHex), Convert.FromHexString(change.BeforeHex), progress, token).ConfigureAwait(false);
            if (result.Success)
                MarkRestored(change);
            return result;
        }
        finally
        {
            _busy.Release();
            if (engineWasRunning)
            {
                try { await _engine.StartAsync().ConfigureAwait(false); } catch { }
            }
        }
    }

    /// <summary>VIN read from the car now (Mode 09 / UDS F190), else the identity already read; null when unknown.</summary>
    private async Task<string?> ReadConnectedVinAsync(CancellationToken token)
    {
        string? vin = null;
        try
        {
            vin = await _vehicle.ReadVinAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Coding VIN read failed: {ex.Message}");
        }

        if (vin is not { Length: 17 })
            vin = _vehicle.CurrentVehicle?.VIN;

        return vin is { Length: 17 } ? vin.Trim().ToUpperInvariant() : null;
    }

    private void MarkRestored(CodingChange change)
    {
        var history = History().ToList();
        foreach (var item in history.Where(h => h.At == change.At && h.OptionId == change.OptionId))
            item.Restored = true;
        SaveHistory(history);
    }

    private async Task<CodingResult> WriteBlockAsync(CodingOption option, byte[] original, byte[] updated, IProgress<string>? progress, CancellationToken token)
    {
        var ext29 = option.TxId > 0x7FF;
        var address = AddressOf(option.TxId, option.RxId, ext29);
        var did = option.DidId;
        var session = ParseByte(option.Session) ?? 0x03;
        if (session is not (0x01 or 0x03))
            return new CodingResult(false, L("მხარდაუჭერელი სესია.", "Unsupported session."));

        try
        {
            progress?.Report(L("ვხსნი დიაგნოსტიკურ სესიას…", "Opening diagnostic session…"));
            if (session == 0x03)
            {
                var raw = await _obd.SendCodingRequestAsync(address, new byte[] { 0x10, 0x03 }, WriteTimeout, token).ConfigureAwait(false);
                var reply = ParseReply(raw, option.RxId, 0x10, new byte[] { 0x03 });
                if (reply.Data is null)
                    return new CodingResult(false, L($"მოდულმა სესია არ გახსნა ({reply.Error}). არაფერი შეცვლილა.", $"Module refused the session ({reply.Error}). Nothing was changed."));
            }

            progress?.Report(L("ვწერ…", "Writing…"));
            var write = await WriteRawAsync(address, did, updated, token).ConfigureAwait(false);
            if (write.Data is null)
            {
                var why = write.Nrc switch
                {
                    0x33 => L("საჭიროა დაცვის კოდი (security access)", "security access required"),
                    0x22 => L("პირობები არ არის შესრულებული", "conditions not correct"),
                    0x31 => L("მნიშვნელობა მიუღებელია", "value out of range"),
                    0x7F or 0x7E => L("ამ სესიაში არ მუშაობს", "not supported in this session"),
                    _ => write.Error
                };
                return new CodingResult(false, L($"მოდულმა ჩაწერა უარყო: {why}. არაფერი შეცვლილა.", $"Module rejected the write: {why}. Nothing was changed."));
            }

            progress?.Report(L("ვამოწმებ ჩაწერილს…", "Verifying…"));
            await Task.Delay(150, token).ConfigureAwait(false);
            var check = await ReadDidAsync(address, did, token).ConfigureAwait(false);
            if (check.IsRead && check.Hex == Convert.ToHexString(updated))
                return new CodingResult(true, L("შეცვლილია და შემოწმებულია. ცვლილება ზოგჯერ ანთების გამორთვა-ჩართვის შემდეგ ჩანს.",
                    "Changed and verified. Some changes appear after switching the ignition off and on."));

            // Read-back mismatch: put the original back.
            progress?.Report(L("შემოწმება ვერ გაიარა — ვაბრუნებ თავდაპირველს…", "Verification failed — restoring the original…"));
            var restore = await WriteRawAsync(address, did, original, token).ConfigureAwait(false);
            var after = await ReadDidAsync(address, did, token).ConfigureAwait(false);
            return after.IsRead && after.Hex == Convert.ToHexString(original)
                ? new CodingResult(false, L("ჩაწერილი მნიშვნელობა არ დადასტურდა, თავდაპირველი აღდგენილია.", "The written value did not verify; the original was restored."))
                : new CodingResult(false, L($"ყურადღება: აღდგენა ვერ დადასტურდა ({restore.Error}). გამოიყენეთ სარეზერვო ასლი ისტორიიდან.",
                    $"Warning: restore could not be verified ({restore.Error}). Use the backup from history."));
        }
        finally
        {
            try
            {
                await _obd.SendCodingRequestAsync(address, new byte[] { 0x10, 0x01 }, ReadTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The module returns to the default session by itself after ~5 s.
            }
        }
    }

    private async Task<CodingReply> WriteRawAsync(ServiceDiagnosticAddress address, int did, byte[] data, CancellationToken token)
    {
        var payload = new byte[3 + data.Length];
        payload[0] = 0x2E;
        payload[1] = (byte)(did >> 8);
        payload[2] = (byte)did;
        Array.Copy(data, 0, payload, 3, data.Length);
        var raw = await _obd.SendCodingRequestAsync(address, payload, WriteTimeout, token).ConfigureAwait(false);
        var reply = ParseReply(raw, address.ReceiveCanId, 0x2E, new[] { (byte)(did >> 8), (byte)did });
        // A positive write reply is "6E DID" (Data = empty array).
        return reply;
    }

    /// <summary>Plain-text export of a backup (for sharing / sending as evidence).</summary>
    public static string BuildText(CodingBackup backup)
    {
        var lines = new List<string>
        {
            $"OBD360 coding backup · {backup.CreatedAt:yyyy-MM-dd HH:mm}",
            $"{backup.Vehicle} · VIN {backup.Vin}",
            backup.Note,
            string.Empty
        };
        foreach (var module in backup.Modules)
        {
            lines.Add($"[{module.Name}] TX 0x{module.Tx:X} RX 0x{module.Rx:X}{(module.Extended29Bit ? " 29-bit" : string.Empty)} PN {module.PartNumber} SW {module.SoftwareVersion}");
            foreach (var value in module.Values.Where(v => v.IsRead))
                lines.Add($"  {value.Did} = {value.Hex}");
        }
        return string.Join(Environment.NewLine, lines);
    }
}
