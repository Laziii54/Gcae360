using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Brand-agnostic OEM profile infrastructure. Generic OBD-II remains the universal
/// baseline; brand profiles only unlock OEM architecture/capability metadata after
/// a verified vehicle/ECU transport definition is available.
/// </summary>
public interface IOemDiagnosticsProfile
{
    string Id { get; }
    string DisplayName { get; }
    bool Matches(VehicleInfo vehicle);
    IReadOnlyList<VehicleDiagnosticModule> GetModules();
    IReadOnlyList<VehicleCapability> GetCapabilities(string ecuId);
}

public sealed class UniversalOemProfile : IOemDiagnosticsProfile
{
    public string Id => "UNIVERSAL-OEM";
    public string DisplayName => "Universal OEM Diagnostics (capability-gated)";
    public bool Matches(VehicleInfo vehicle) => true;

    public IReadOnlyList<VehicleDiagnosticModule> GetModules() => VehicleModuleCatalog.Modules;
    public IReadOnlyList<VehicleCapability> GetCapabilities(string ecuId) => VehicleCapabilityCatalog.Build(Id, ecuId);
}

public sealed class BrandOemProfile : IOemDiagnosticsProfile
{
    public string Id { get; }
    public string DisplayName { get; }
    private readonly IReadOnlyList<string> _manufacturerNames;

    public BrandOemProfile(string id, string displayName, string manufacturer, params string[] aliases)
    {
        Id = id;
        DisplayName = displayName;
        _manufacturerNames = new[] { manufacturer }
            .Concat(aliases ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool Matches(VehicleInfo vehicle) =>
        !string.IsNullOrWhiteSpace(vehicle.Manufacturer) &&
        _manufacturerNames.Any(name =>
            vehicle.Manufacturer.Contains(name, StringComparison.OrdinalIgnoreCase) ||
            name.Contains(vehicle.Manufacturer, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<VehicleDiagnosticModule> GetModules() => VehicleModuleCatalog.Modules;
    public IReadOnlyList<VehicleCapability> GetCapabilities(string ecuId) => VehicleCapabilityCatalog.Build(Id, ecuId);
}

public static class VehicleModuleCatalog
{
    public static readonly IReadOnlyList<VehicleDiagnosticModule> Modules = new[]
    {
        Module("POWERTRAIN", "Powertrain", "Engine / Powertrain", VehicleModuleType.Powertrain),
        Module("TRANSMISSION", "Transmission", "Transmission Control Module", VehicleModuleType.Tcm),
        Module("ABS", "ABS / Brakes", "Anti-lock / Electronic Brake Control", VehicleModuleType.AbsBrakes),
        Module("SRS", "SRS / Airbag", "Supplemental Restraint System", VehicleModuleType.SrsAirbag),
        Module("BCM", "BCM / Body", "Body Control Module", VehicleModuleType.BcmBody),
        Module("EPS", "Electric Steering", "Electric Power Steering", VehicleModuleType.Eps),
        Module("HV", "Hybrid / EV / HV", "Hybrid / High Voltage Control", VehicleModuleType.HybridHv),
        Module("HVAC", "HVAC", "Climate Control", VehicleModuleType.Hvac),
        Module("CLUSTER", "Cluster", "Instrument Cluster", VehicleModuleType.Cluster),
        Module("TPMS", "TPMS", "Tire Pressure Monitoring", VehicleModuleType.Tpms),
        Module("ADAS", "ADAS", "Driver Assistance Controllers", VehicleModuleType.Adas),
        Module("INFOTAINMENT", "Infotainment", "Infotainment / Telematics", VehicleModuleType.Infotainment),
        Module("OTHER", "Other Modules", "Other manufacturer modules", VehicleModuleType.Other)
    };

    private static VehicleDiagnosticModule Module(string id, string name, string ecuName, VehicleModuleType type) =>
        new()
        {
            Id = id,
            Name = name,
            EcuName = ecuName,
            Type = type,
            SupportedFunctions = new[]
            {
                "IDENTIFICATION.READ",
                "DTC.READ",
                "DATA.READ",
                "CAPABILITY.DISCOVERY"
            }
        };
}

public static class VehicleCapabilityCatalog
{
    public static IReadOnlyList<VehicleCapability> Build(string profileId, string ecuId) => new[]
    {
        new VehicleCapability
        {
            Code = "IDENTIFICATION.READ", Ecu = ecuId, VehicleProfile = profileId,
            Transport = "CAN / ISO-TP / OEM transport (verified address required)", Protocol = "UDS/OEM",
            ReadSupported = true, SafetyLevel = "READ_ONLY", EnabledByDefault = true
        },
        new VehicleCapability
        {
            Code = "DTC.READ", Ecu = ecuId, VehicleProfile = profileId,
            Transport = "CAN / ISO-TP / OEM transport (verified address required)", Protocol = "UDS/OEM",
            ReadSupported = true, SafetyLevel = "READ_ONLY", EnabledByDefault = true
        },
        new VehicleCapability
        {
            Code = "DATA.READ", Ecu = ecuId, VehicleProfile = profileId,
            Transport = "CAN / ISO-TP / OEM transport (verified address required)", Protocol = "UDS/OEM",
            ReadSupported = true, LiveDataSupported = true, SafetyLevel = "READ_ONLY", EnabledByDefault = true
        },
        new VehicleCapability
        {
            Code = "FREEZEFRAME.READ", Ecu = ecuId, VehicleProfile = profileId,
            Transport = "CAN / ISO-TP / OEM transport (verified address required)", Protocol = "UDS/OEM",
            ReadSupported = true, SafetyLevel = "READ_ONLY", EnabledByDefault = true
        },
        new VehicleCapability
        {
            Code = "DTC.CLEAR", Ecu = ecuId, VehicleProfile = profileId,
            Transport = "CAN / ISO-TP / OEM transport (verified address required)", Protocol = "UDS/OEM",
            ReadSupported = true, ClearSupported = true, SafetyLevel = "SERVICE",
            RequiresVoltageCheck = true, EnabledByDefault = false
        },
        new VehicleCapability
        {
            Code = "ROUTINE.EXECUTE", Ecu = ecuId, VehicleProfile = profileId,
            Transport = "CAN / ISO-TP / OEM transport (verified address required)", Protocol = "UDS/OEM",
            RoutineSupported = true, SafetyLevel = "ACTUATOR_TEST",
            RequiresVoltageCheck = true, RequiresSecurityAccess = true, EnabledByDefault = false
        },
        new VehicleCapability
        {
            Code = "CODING.WRITE", Ecu = ecuId, VehicleProfile = profileId,
            Transport = "CAN / ISO-TP / OEM transport (verified address required)", Protocol = "UDS/OEM",
            WriteSupported = true, SafetyLevel = "CODING / WRITE",
            RequiresVoltageCheck = true, RequiresSecurityAccess = true, EnabledByDefault = false
        }
    };
}

public sealed class OemProfileResolver
{
    private readonly IReadOnlyList<IOemDiagnosticsProfile> _profiles;

    public OemProfileResolver()
    {
        _profiles = new IOemDiagnosticsProfile[]
        {
            new BrandOemProfile("ABARTH", "Abarth OEM Diagnostics", "Abarth"),
            new BrandOemProfile("BMW", "BMW OEM Diagnostics", "BMW"),
            new BrandOemProfile("BENTLEY", "Bentley OEM Diagnostics", "Bentley"),
            new BrandOemProfile("BAIC", "BAIC OEM Diagnostics", "BAIC"),
            new BrandOemProfile("MERCEDES", "Mercedes-Benz OEM Diagnostics", "Mercedes", "Mercedes-Benz", "Daimler", "MB"),
            new BrandOemProfile("SUBARU", "Subaru OEM Diagnostics", "Subaru"),
            new BrandOemProfile("VOLVO", "Volvo OEM Diagnostics", "Volvo"),
            new BrandOemProfile("TOYOTA", "Toyota OEM Diagnostics", "Toyota"),
            new BrandOemProfile("LEXUS", "Lexus OEM Diagnostics", "Lexus"),
            new BrandOemProfile("HONDA", "Honda OEM Diagnostics", "Honda"),
            new BrandOemProfile("ACURA", "Acura OEM Diagnostics", "Acura"),
            new BrandOemProfile("NISSAN", "Nissan OEM Diagnostics", "Nissan"),
            new BrandOemProfile("INFINITI", "Infiniti OEM Diagnostics", "Infiniti"),
            new BrandOemProfile("MAZDA", "Mazda OEM Diagnostics", "Mazda"),
            new BrandOemProfile("FORD", "Ford OEM Diagnostics", "Ford"),
            new BrandOemProfile("LINCOLN", "Lincoln OEM Diagnostics", "Lincoln"),
            new BrandOemProfile("CHEVROLET", "Chevrolet OEM Diagnostics", "Chevrolet", "Chevy"),
            new BrandOemProfile("GMC", "GMC OEM Diagnostics", "GMC", "General Motors"),
            new BrandOemProfile("CADILLAC", "Cadillac OEM Diagnostics", "Cadillac"),
            new BrandOemProfile("BUICK", "Buick OEM Diagnostics", "Buick"),
            new BrandOemProfile("GM", "General Motors OEM Diagnostics", "General Motors", "GM"),
            new BrandOemProfile("VOLKSWAGEN", "Volkswagen OEM Diagnostics", "Volkswagen", "VW", "VAG"),
            new BrandOemProfile("AUDI", "Audi OEM Diagnostics", "Audi"),
            new BrandOemProfile("PORSCHE", "Porsche OEM Diagnostics", "Porsche"),
            new BrandOemProfile("SKODA", "Škoda OEM Diagnostics", "Skoda", "Škoda"),
            new BrandOemProfile("SEAT", "SEAT OEM Diagnostics", "SEAT"),
            new BrandOemProfile("HYUNDAI", "Hyundai OEM Diagnostics", "Hyundai"),
            new BrandOemProfile("KIA", "Kia OEM Diagnostics", "Kia"),
            new BrandOemProfile("GENESIS", "Genesis OEM Diagnostics", "Genesis"),
            new BrandOemProfile("RENAULT", "Renault OEM Diagnostics", "Renault"),
            new BrandOemProfile("DACIA", "Dacia OEM Diagnostics", "Dacia"),
            new BrandOemProfile("PEUGEOT", "Peugeot OEM Diagnostics", "Peugeot"),
            new BrandOemProfile("CITROEN", "Citroën OEM Diagnostics", "Citroen", "Citroën"),
            new BrandOemProfile("FIAT", "Fiat OEM Diagnostics", "Fiat"),
            new BrandOemProfile("ALFA-ROMEO", "Alfa Romeo OEM Diagnostics", "Alfa Romeo"),
            new BrandOemProfile("JEEP", "Jeep OEM Diagnostics", "Jeep"),
            new BrandOemProfile("DODGE", "Dodge OEM Diagnostics", "Dodge"),
            new BrandOemProfile("CHRYSLER", "Chrysler OEM Diagnostics", "Chrysler"),
            new BrandOemProfile("CUPRA", "Cupra OEM Diagnostics", "Cupra"),
            new BrandOemProfile("DAEWOO", "Daewoo OEM Diagnostics", "Daewoo"),
            new BrandOemProfile("DONGFENG", "Dongfeng OEM Diagnostics", "Dongfeng"),
            new BrandOemProfile("DS", "DS Automobiles OEM Diagnostics", "DS Automobiles", "DS"),
            new BrandOemProfile("FERRARI", "Ferrari OEM Diagnostics", "Ferrari"),
            new BrandOemProfile("LAND-ROVER", "Land Rover OEM Diagnostics", "Land Rover"),
            new BrandOemProfile("JAGUAR", "Jaguar OEM Diagnostics", "Jaguar"),
            new BrandOemProfile("JAC", "JAC OEM Diagnostics", "JAC"),
            new BrandOemProfile("LADA", "Lada OEM Diagnostics", "Lada"),
            new BrandOemProfile("LAMBORGHINI", "Lamborghini OEM Diagnostics", "Lamborghini"),
            new BrandOemProfile("LANCIA", "Lancia OEM Diagnostics", "Lancia"),
            new BrandOemProfile("MITSUBISHI", "Mitsubishi OEM Diagnostics", "Mitsubishi"),
            new BrandOemProfile("SUZUKI", "Suzuki OEM Diagnostics", "Suzuki"),
            new BrandOemProfile("ISUZU", "Isuzu OEM Diagnostics", "Isuzu"),
            new BrandOemProfile("SAAB", "Saab OEM Diagnostics", "Saab"),
            new BrandOemProfile("LUCID", "Lucid OEM Diagnostics", "Lucid"),
            new BrandOemProfile("MASERATI", "Maserati OEM Diagnostics", "Maserati"),
            new BrandOemProfile("MCLAREN", "McLaren OEM Diagnostics", "McLaren"),
            new BrandOemProfile("NIO", "NIO OEM Diagnostics", "NIO"),
            new BrandOemProfile("POLESTAR", "Polestar OEM Diagnostics", "Polestar"),
            new BrandOemProfile("RAM", "Ram OEM Diagnostics", "Ram"),
            new BrandOemProfile("RIVIAN", "Rivian OEM Diagnostics", "Rivian"),
            new BrandOemProfile("ROLLS-ROYCE", "Rolls-Royce OEM Diagnostics", "Rolls-Royce", "Rolls Royce"),
            new BrandOemProfile("SMART", "Smart OEM Diagnostics", "Smart"),
            new BrandOemProfile("UAZ", "UAZ OEM Diagnostics", "UAZ"),
            new BrandOemProfile("ZEEKR", "Zeekr OEM Diagnostics", "Zeekr"),
            new BrandOemProfile("HONGQI", "Hongqi OEM Diagnostics", "Hongqi"),
            // These profiles deliberately provide capability metadata only. OEM
            // access remains locked until a verified ECU address/definition is
            // registered for the exact model and diagnostic transport.
            new BrandOemProfile("MINI", "MINI OEM Diagnostics", "MINI"),
            new BrandOemProfile("OPEL", "Opel OEM Diagnostics", "Opel", "Vauxhall"),
            new BrandOemProfile("KGM", "KGM / SsangYong OEM Diagnostics", "KGM", "SsangYong"),
            new BrandOemProfile("TESLA", "Tesla OEM Diagnostics", "Tesla"),
            new BrandOemProfile("BYD", "BYD OEM Diagnostics", "BYD"),
            new BrandOemProfile("CHERY", "Chery OEM Diagnostics", "Chery"),
            new BrandOemProfile("GEELY", "Geely OEM Diagnostics", "Geely"),
            new BrandOemProfile("HAVAL", "Haval / GWM OEM Diagnostics", "Haval", "Great Wall", "GWM"),
            new BrandOemProfile("MG", "MG OEM Diagnostics", "MG", "Morris Garages"),
            new UniversalOemProfile()
        };
    }

    public IOemDiagnosticsProfile Resolve(VehicleInfo vehicle) => _profiles.First(x => x.Matches(vehicle));
}
