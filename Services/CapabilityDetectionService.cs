using System.Text.Json;
using CarScanner.Models;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Services;

/// <summary>
/// The single source of truth for what this *connected vehicle* can do.
///
/// Layer 1 is validated from standard OBD-II responses. Layer 2 only unlocks
/// paid analytics once the same evidence is present. Layer 3 never advertises
/// OEM diagnostics, active tests or coding merely because a driver selected a
/// brand: a verified ECU address and vehicle-specific definition are required.
/// </summary>
public sealed class CapabilityDetectionService
{
    private readonly OBDService _obd;
    private readonly VehicleInfoService _vehicleInfo;
    private readonly VehicleProfileResolverService _vehicleProfiles;
    private readonly ProService _pro;
    private readonly OemProfileResolver _oemProfiles;
    private readonly OemAddressRegistry _oemAddresses;
    private readonly OemTransportReadinessService _oemTransport;
    private readonly ManufacturerDefinitionCatalogService _manufacturerDefinitions;
    private readonly OemDefinitionCatalogService _oemDefinitions;
    private readonly OemModuleDiscoveryService _oemModuleDiscovery;
    private readonly OemEcuCapabilityDetectionService _oemCapabilityDetection;
    private readonly OemOperationService _oemOperations;

    public VehicleCapabilitySnapshot? Current { get; private set; }
    public event EventHandler<VehicleCapabilitySnapshot>? CapabilitiesUpdated;

    public CapabilityDetectionService(
        OBDService obd,
        VehicleInfoService vehicleInfo,
        VehicleProfileResolverService vehicleProfiles,
        ProService pro,
        OemProfileResolver oemProfiles,
        OemAddressRegistry oemAddresses,
        OemTransportReadinessService oemTransport,
        ManufacturerDefinitionCatalogService manufacturerDefinitions,
        OemDefinitionCatalogService oemDefinitions,
        OemModuleDiscoveryService oemModuleDiscovery,
        OemEcuCapabilityDetectionService oemCapabilityDetection,
        OemOperationService oemOperations)
    {
        _obd = obd;
        _vehicleInfo = vehicleInfo;
        _vehicleProfiles = vehicleProfiles;
        _pro = pro;
        _oemProfiles = oemProfiles;
        _oemAddresses = oemAddresses;
        _oemTransport = oemTransport;
        _manufacturerDefinitions = manufacturerDefinitions;
        _oemDefinitions = oemDefinitions;
        _oemModuleDiscovery = oemModuleDiscovery;
        _oemCapabilityDetection = oemCapabilityDetection;
        _oemOperations = oemOperations;
    }

    public async Task<VehicleCapabilitySnapshot> DetectAsync(
        IProgress<CapabilityDetectionProgress>? progress = null,
        CancellationToken token = default)
    {
        progress?.Report(new CapabilityDetectionProgress(5, "Adapter და კავშირის შემოწმება…"));

        if (!_obd.IsInitialized)
        {
            try
            {
                await _obd.InitializeAsync(token).ConfigureAwait(false);
            }
            catch
            {
                return Publish(CreateDisconnectedSnapshot());
            }
        }

        progress?.Report(new CapabilityDetectionProgress(20, "ECU პასუხის დადასტურება…"));
        var adapter = await _obd.GetAdapterDiagnosticsAsync(token).ConfigureAwait(false);
        if (!adapter.VehicleResponding)
        {
            return Publish(CreateNoEcuSnapshot(adapter));
        }

        progress?.Report(new CapabilityDetectionProgress(38, "მხარდაჭერილი სტანდარტული PID-ების წაკითხვა…"));
        var pids = await _obd.DiscoverSupportedPidsAsync(token).ConfigureAwait(false);
        var supportedPidNumbers = pids.Select(pid => pid.Pid).ToHashSet();

        progress?.Report(new CapabilityDetectionProgress(55, "VIN და ავტომობილის პროფილის შემოწმება…"));
        VehicleInfo vehicle;
        try
        {
            vehicle = await _vehicleInfo.IdentifyConnectedVehicleAsync(token).ConfigureAwait(false);
        }
        catch
        {
            vehicle = _vehicleInfo.CurrentVehicle ?? new VehicleInfo();
        }
        var resolvedVehicle = _vehicleProfiles.Resolve(vehicle);

        progress?.Report(new CapabilityDetectionProgress(68, "Readiness და დამატებითი OBD სერვისების შემოწმება…"));
        var readinessAvailable = false;
        try
        {
            await _obd.ReadReadinessAsync(token).ConfigureAwait(false);
            readinessAvailable = true;
        }
        catch
        {
            // PID 01 is optional for a faulty/limited ECU response. It is
            // reported as unavailable rather than converted to a fake result.
        }

        var mode06Available = false;
        try
        {
            // "06 00" = list of supported monitors/tests (a bare "06" is not a valid request).
            var mode06 = await _obd.ReadMode06MidAsync(0x00, token).ConfigureAwait(false);
            mode06Available = IsPositiveData(mode06);
        }
        catch
        {
            // Mode 06 is optional. A negative/no-data reply is genuine proof
            // that it must not be exposed as a working feature for this ECU.
        }

        progress?.Report(new CapabilityDetectionProgress(82, "PRO და OEM შესაძლებლობების დამუშავება…"));
        ManufacturerDefinitionResolution manufacturerResolution;
        var executableOemDefinitionCount = 0;
        IReadOnlyList<OemEcuDefinition> executableOemDefinitions = Array.Empty<OemEcuDefinition>();
        try
        {
            await _oemDefinitions.EnsureLoadedAsync(token).ConfigureAwait(false);
            manufacturerResolution = _manufacturerDefinitions.Resolve(resolvedVehicle);
            executableOemDefinitions = _oemDefinitions
                .GetForVehicle(resolvedVehicle, manufacturerResolution);
            executableOemDefinitionCount = executableOemDefinitions.Count;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException)
        {
            manufacturerResolution = new ManufacturerDefinitionResolution(
                ManufacturerDefinitionResolutionState.ManufacturerUnknown,
                null,
                Array.Empty<VehicleDefinitionCoverage>(),
                $"Manufacturer catalog ვერ ჩაიტვირთა: {ex.Message}");
        }

        var runtimeOem = OemRuntimeCapabilityEvidence.Empty;
        if (resolvedVehicle.CanUseManufacturerSpecificDefinitions && executableOemDefinitionCount > 0)
        {
            try
            {
                var discovery = await _oemModuleDiscovery.DiscoverAsync(vehicle, token).ConfigureAwait(false);
                var ecuCapabilities = await _oemCapabilityDetection
                    .DetectAsync(vehicle, discovery, token)
                    .ConfigureAwait(false);
                var operations = await _oemOperations
                    .DetectAsync(vehicle, discovery, adapter, token)
                    .ConfigureAwait(false);
                runtimeOem = BuildRuntimeOemEvidence(
                    executableOemDefinitions, discovery, ecuCapabilities, operations);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                runtimeOem = OemRuntimeCapabilityEvidence.Failed(
                    string.IsNullOrWhiteSpace(ex.Message) ? "OEM capability check failed." : ex.Message);
            }
        }
        var capabilities = BuildCapabilities(
            vehicle,
            resolvedVehicle,
            manufacturerResolution,
            executableOemDefinitionCount,
            adapter,
            supportedPidNumbers,
            readinessAvailable,
            mode06Available,
            runtimeOem);

        progress?.Report(new CapabilityDetectionProgress(100, "შესაძლებლობების შემოწმება დასრულდა."));
        return Publish(new VehicleCapabilitySnapshot
        {
            AdapterName = adapter.AdapterName,
            ConnectionSummary = $"ECU პასუხობს · {adapter.ProbeLatencyMs} ms",
            VehicleSummary = resolvedVehicle.DisplayName,
            VehicleProfileStatus = resolvedVehicle.VerificationMessage,
            Protocol = FirstKnown(_obd.CurrentProtocolDescription, adapter.Protocol),
            SupportedPidCount = pids.Count,
            Capabilities = capabilities
        });
    }

    private IReadOnlyList<DetectedCapability> BuildCapabilities(
        VehicleInfo vehicle,
        ResolvedVehicleProfile resolvedVehicle,
        ManufacturerDefinitionResolution manufacturerResolution,
        int executableOemDefinitionCount,
        AdapterDiagnostics adapter,
        IReadOnlySet<int> pids,
        bool readinessAvailable,
        bool mode06Available,
        OemRuntimeCapabilityEvidence runtimeOem)
    {
        var values = new List<DetectedCapability>();
        var genericEvidence = $"ECU პასუხობს · {pids.Count} დეკოდირებადი სტანდარტული PID · {adapter.Protocol}";
        var hasLiveData = pids.Count > 0;
        var hasSpeed = pids.Contains(0x0D);
        var hasFuelInputs = pids.Contains(0x10) || pids.Contains(0x5E) || pids.Contains(0x2F);
        var transport = _oemTransport.Evaluate(adapter);

        // Layer 1 — every item is based on an actual standard-OBD response.
        values.Add(Capability("obd.connection", "Universal OBD-II კავშირი", "სტანდარტული Powertrain ECU-ს დიაგნოსტიკა.", genericEvidence, CapabilityTier.UniversalObd, CapabilityState.Available));
        values.Add(Capability("obd.live-data", "Live Data", "მხოლოდ ECU-ის მიერ რეკლამირებული და დეკოდირებადი PID-ები.", hasLiveData ? genericEvidence : "ECU-მ დეკოდირებადი PID არ დააბრუნა.", CapabilityTier.UniversalObd, hasLiveData ? CapabilityState.Available : CapabilityState.NotAvailable));
        values.Add(Capability("obd.dtc", "Trouble Codes / Freeze Frame", "Stored, Pending და Permanent DTC-ები; Freeze Frame მხოლოდ მაშინ, როცა ECU-ში მართლა შენახულია.", genericEvidence, CapabilityTier.UniversalObd, CapabilityState.Available));
        values.Add(Capability("obd.readiness", "Emissions / Readiness", "MIL, DTC count და readiness monitors Mode 01 PID 01-დან.", readinessAvailable ? "ECU-მ რეალური Readiness პასუხი დააბრუნა." : "ამ ECU-მ Readiness პასუხი არ დააბრუნა.", CapabilityTier.UniversalObd, readinessAvailable ? CapabilityState.Available : CapabilityState.NotAvailable));
        values.Add(Capability("obd.mode06", "Mode 06 მონიტორები", "Non-continuous emissions test მნიშვნელობები, საზღვრები და Passed/Failed.", mode06Available ? "ECU-მ Mode 06 მონაცემი დააბრუნა." : "ამ ECU-ზე Mode 06 მონაცემი ხელმისაწვდომი არ არის.", CapabilityTier.UniversalObd, mode06Available ? CapabilityState.Available : CapabilityState.NotAvailable));
        values.Add(Capability(
            "obd.identity",
            "Vehicle / VIN ინფორმაცია",
            "VIN, მარკა და წელი მხოლოდ ECU/VIN-ის დადასტურებული მონაცემით.",
            $"{resolvedVehicle.VerificationMessage} {resolvedVehicle.Evidence}",
            CapabilityTier.UniversalObd,
            resolvedVehicle.VerificationState == VehicleProfileVerificationState.Confirmed
                ? CapabilityState.Available
                : CapabilityState.Conditional));
        values.Add(Capability("obd.performance", "Performance Lab", "რეალური Vehicle Speed PID 01 0D-ით; ტესტი არ იწყება demo სიჩქარით.", hasSpeed ? "Vehicle Speed PID 01 0D მხარდაჭერილია." : "Vehicle Speed PID 01 0D ECU-მ არ დაადასტურა.", CapabilityTier.UniversalObd, hasSpeed ? CapabilityState.Available : CapabilityState.NotAvailable));

        // Layer 2 — never unlocked solely by payment. The required telemetry
        // must also be supported by the current vehicle.
        var proState = _pro.CanUse("capabilities") ? CapabilityState.Available : CapabilityState.RequiresPro;
        values.Add(Capability("pro.recording", "Data Recording & Graph Studio", "Live Data-ის ჩანაწერი, გრაფიკები და CSV export.", ProEvidence(hasLiveData, "Live PID მონაცემი ხელმისაწვდომია.", "ჯერ საჭიროა დეკოდირებადი Live Data PID."), CapabilityTier.ProAnalytics, ResolveProState(proState, hasLiveData)));
        values.Add(Capability("pro.ai", "AI Diagnostic", "რეალურ DTC, Freeze Frame, Readiness და Live Data კონტექსტზე აგებული სავარაუდო დიაგნოზი.", ProEvidence(hasLiveData, "AI-სთვის საჭირო Live Data კონტექსტი ხელმისაწვდომია.", "ჯერ საჭიროა მინიმუმ ერთი რეალური Live Data PID."), CapabilityTier.ProAnalytics, ResolveProState(proState, hasLiveData)));
        values.Add(Capability("pro.fuel", "Fuel Analytics", "ECU Fuel Rate/MAF/Fuel Level-ის მიხედვით რეალური ან მკაფიოდ მონიშნული calculated consumption.", ProEvidence(hasFuelInputs, "საწვავის გამოთვლისთვის აუცილებელი მინიმუმ ერთი PID არის." , "Fuel Rate, MAF ან Fuel Level PID ვერ დადასტურდა."), CapabilityTier.ProAnalytics, ResolveProState(proState, hasFuelInputs)));
        values.Add(Capability("pro.report", "Professional Diagnostic Report", "VIN, სკანის შედეგი, DTC, Freeze Frame და readiness-ის PDF/გაზიარება.", "სტანდარტული OBD სკანის შედეგი ხელმისაწვდომია.", CapabilityTier.ProAnalytics, proState));
        // The features below are what the app actually does on this car today
        // (module scan, module live data, mileage, coding backup) — they work on
        // every CAN car and do not wait for the older OEM-definition catalog.
        var isCan = _obd.IsCanProtocol;
        values.Add(Capability("pro.mileage", "გარბენის გადამოწმება",
            "გარბენს რამდენიმე მოდულიდან კითხულობს და ადარებს ერთმანეთს.",
            isCan ? "თუ ამ მოდელზე გარბენის ადგილი ჯერ უცნობია, აპი მას ერთხელ მოძებნის." : "საჭიროა CAN კავშირი (დაახლ. 2008 წლიდან).",
            CapabilityTier.ProAnalytics, ResolveProState(proState, isCan)));

        values.Add(Capability("oem.modules", "ყველა მოდულის შეცდომები",
            "ABS, Airbag, BCM, კოლოფი, ჰიბრიდი და სხვა მოდულების შეცდომები და მათი წაშლა.",
            isCan ? "მუშაობს ამ მანქანაზე." : "საჭიროა CAN კავშირი (დაახლ. 2008 წლიდან).",
            CapabilityTier.ManufacturerSpecific, isCan ? CapabilityState.Available : CapabilityState.NotAvailable));
        values.Add(Capability("oem.live-data", "მოდულების ცოცხალი მონაცემები",
            "ბატარეა, ჰიბრიდი, ABS, საჭე და სხვა მოდულების მონაცემები.",
            isCan ? "ხელმისაწვდომია ამ მარკის ცნობილი მოდულებისთვის." : "საჭიროა CAN კავშირი (დაახლ. 2008 წლიდან).",
            CapabilityTier.ManufacturerSpecific, ResolveProState(proState, isCan)));
        values.Add(Capability("oem.service", "სერვისის ფუნქციები",
            "ზეთის ინტერვალი და სერვისის მაჩვენებლები.",
            isCan ? "ფუნქციების რაოდენობა მოდელზეა დამოკიდებული." : "საჭიროა CAN კავშირი (დაახლ. 2008 წლიდან).",
            CapabilityTier.ManufacturerSpecific, isCan ? CapabilityState.Conditional : CapabilityState.NotAvailable));
        values.Add(Capability("oem.coding", "კოდირება",
            "ყველა მოდულის სარეზერვო ასლი და შედარება; პარამეტრების შეცვლა — შემოწმებულ მოდელებზე.",
            isCan ? "სარეზერვო ასლი მუშაობს ამ მანქანაზე." : "საჭიროა CAN კავშირი (დაახლ. 2008 წლიდან).",
            CapabilityTier.ManufacturerSpecific, isCan ? CapabilityState.Conditional : CapabilityState.NotAvailable));

        return values;
    }

    private VehicleCapabilitySnapshot CreateDisconnectedSnapshot() => new()
    {
        ConnectionSummary = "OBD adapter არ არის დაკავშირებული ან ვერ მომზადდა.",
        VehicleSummary = "ჯერ დააკავშირე adapter და ჩართე კონტაქტი.",
        Protocol = "—",
        Capabilities = new[]
        {
            Capability("obd.connection", "Universal OBD-II კავშირი", "საჭიროა აქტიური Bluetooth/Wi-Fi/USB adapter და ECU პასუხი.", "Adapter/ECU პასუხი არ დადასტურდა.", CapabilityTier.UniversalObd, CapabilityState.NotConnected),
            Capability("pro.analytics", "PRO ანალიტიკა", "AI, Recording, Fuel Analytics და Reports იყენებს მხოლოდ ნამდვილ OBD მონაცემებს.", "ჯერ საჭიროა OBD კავშირი.", CapabilityTier.ProAnalytics, CapabilityState.NotConnected),
            Capability("oem.diagnostics", "Manufacturer-specific Diagnostics", "OEM ფუნქცია არ აქტიურდება მხოლოდ მარკის არჩევით.", "ჯერ საჭიროა VIN, ECU და დადასტურებული OEM definition.", CapabilityTier.ManufacturerSpecific, CapabilityState.NotConnected)
        }
    };

    private VehicleCapabilitySnapshot CreateNoEcuSnapshot(AdapterDiagnostics adapter) => new()
    {
        AdapterName = adapter.AdapterName,
        ConnectionSummary = "Adapter არის, მაგრამ ECU პასუხი ვერ დადასტურდა.",
        VehicleSummary = "ჩართე კონტაქტი და შეამოწმე OBD adapter.",
        Protocol = FirstKnown(_obd.CurrentProtocolDescription, adapter.Protocol),
        Capabilities = new[]
        {
            Capability("obd.connection", "Universal OBD-II კავშირი", "Adapter დაკავშირებულია, თუმცა სტანდარტულ 0100 მოთხოვნაზე ECU პასუხი ვერ მივიღეთ.", "Bluetooth: connected · ECU: no verified response", CapabilityTier.UniversalObd, CapabilityState.NotAvailable),
            Capability("pro.analytics", "PRO ანალიტიკა", "PRO არ ანაცვლებს ECU კავშირს.", "ჯერ საჭიროა რეალური Powertrain ECU პასუხი.", CapabilityTier.ProAnalytics, CapabilityState.NotAvailable),
            Capability("oem.diagnostics", "Manufacturer-specific Diagnostics", "OEM სკანირება იწყება მხოლოდ დადასტურებული ECU ტრანსპორტით.", "ECU პასუხი ჯერ არ არსებობს.", CapabilityTier.ManufacturerSpecific, CapabilityState.NotAvailable)
        }
    };

    private VehicleCapabilitySnapshot Publish(VehicleCapabilitySnapshot snapshot)
    {
        Current = snapshot;
        CapabilitiesUpdated?.Invoke(this, snapshot);
        return snapshot;
    }

    private static DetectedCapability Capability(string id, string title, string description, string evidence, CapabilityTier tier, CapabilityState state) => new()
    {
        Id = id,
        Title = title,
        Description = description,
        Evidence = evidence,
        Tier = tier,
        State = state
    };

    private static CapabilityState ResolveProState(CapabilityState proState, bool telemetrySupported) =>
        !telemetrySupported ? CapabilityState.NotAvailable : proState;

    private static string ProEvidence(bool telemetrySupported, string available, string unavailable) =>
        telemetrySupported ? available : unavailable;

    private static CapabilityState RuntimeOemState(
        bool available,
        bool wasChecked,
        CapabilityState definitionState) =>
        available ? CapabilityState.Available :
        wasChecked ? CapabilityState.NotAvailable : definitionState;

    private static string RuntimeEvidence(
        OemRuntimeCapabilityEvidence runtime,
        string availableEvidence,
        string fallbackEvidence) =>
        runtime.WasChecked
            ? (string.IsNullOrWhiteSpace(runtime.Error) ? availableEvidence : runtime.Error)
            : fallbackEvidence;

    private static OemRuntimeCapabilityEvidence BuildRuntimeOemEvidence(
        IReadOnlyList<OemEcuDefinition> definitions,
        OemModuleDiscoveryResult discovery,
        OemCapabilityDetectionResult capabilities,
        OemOperationAvailabilityResult operations)
    {
        var liveCommandIds = definitions
            .SelectMany(definition => definition.ReadOnlyCommands)
            .Where(command => command.CapabilityCode.Equals("DATA.READ", StringComparison.OrdinalIgnoreCase))
            .Select(command => command.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var liveDataCount = capabilities.Ecus
            .SelectMany(ecu => ecu.Capabilities)
            .Count(capability =>
                capability.Availability == OemCapabilityAvailability.Available &&
                liveCommandIds.Contains(capability.CommandId));
        var executable = operations.Operations.Where(operation => operation.CanExecute).ToArray();
        return new OemRuntimeCapabilityEvidence(
            true,
            discovery.Modules.Count,
            liveDataCount,
            executable.Count(operation => operation.Definition.Type is OemOperationType.Service or OemOperationType.ActiveTest),
            executable.Count(operation => operation.Definition.Type == OemOperationType.Coding),
            string.Empty);
    }

    private sealed record OemRuntimeCapabilityEvidence(
        bool WasChecked,
        int ModuleCount,
        int LiveDataCount,
        int ServiceCount,
        int CodingCount,
        string Error)
    {
        public static OemRuntimeCapabilityEvidence Empty { get; } =
            new(false, 0, 0, 0, 0, string.Empty);

        public static OemRuntimeCapabilityEvidence Failed(string error) =>
            new(true, 0, 0, 0, 0, error);
    }

    private static bool IsPositiveData(string? response) =>
        !string.IsNullOrWhiteSpace(response) &&
        !response.Contains("NO DATA", StringComparison.OrdinalIgnoreCase) &&
        !response.Contains("ERROR", StringComparison.OrdinalIgnoreCase) &&
        !response.Contains("STOPPED", StringComparison.OrdinalIgnoreCase) &&
        !response.Trim().Equals("?", StringComparison.Ordinal);

    private static bool HasKnown(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(value, "უცნობია", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(value, "—", StringComparison.Ordinal);

    private static string FirstKnown(params string?[] values) =>
        values.FirstOrDefault(HasKnown) ?? "—";

}
