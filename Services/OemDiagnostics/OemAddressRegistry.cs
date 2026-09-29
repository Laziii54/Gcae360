using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

public sealed record VerifiedEcuAddress(
    string Brand,
    string VehicleProfile,
    string EcuId,
    int TxCanId,
    int RxCanId,
    bool Extended29Bit,
    string Source,
    string Notes,
    byte[] ReadOnlyProbe,
    string DefinitionId = "",
    int? MinimumYear = null,
    int? MaximumYear = null);

/// <summary>
/// Brand-agnostic verified address registry. It intentionally starts empty:
/// OEM CAN addresses must be populated from verified, vehicle-specific data.
/// No guessed address is ever transmitted by the scanner.
/// </summary>
public sealed class OemAddressRegistry
{
    private readonly Dictionary<string, VerifiedEcuAddress> _entries = new(StringComparer.OrdinalIgnoreCase);

    public void Register(VerifiedEcuAddress address)
    {
        if (string.IsNullOrWhiteSpace(address.Brand) ||
            string.IsNullOrWhiteSpace(address.VehicleProfile) ||
            string.IsNullOrWhiteSpace(address.EcuId) ||
            string.IsNullOrWhiteSpace(address.Source) ||
            address.ReadOnlyProbe is null ||
            address.ReadOnlyProbe.Length == 0)
        {
            throw new ArgumentException(
                "Brand, vehicle profile, ECU id, source and a read-only probe are required.");
        }

        // Consumer discovery can only transmit UDS ReadDataByIdentifier (22)
        // or ReadDTCInformation (19). Sessions, routines, security and write
        // services must never be registered as an automatic probe.
        if (address.ReadOnlyProbe[0] is not (0x22 or 0x19))
            throw new ArgumentException(
                "An ECU discovery probe must be a read-only UDS service (0x22 or 0x19).",
                nameof(address));

        if (address.TxCanId < 0 ||
            address.RxCanId < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(address),
                "CAN address cannot be negative.");
        }

        if (!address.Extended29Bit &&
            (address.TxCanId > 0x7FF ||
             address.RxCanId > 0x7FF))
        {
            throw new ArgumentOutOfRangeException(
                nameof(address),
                "11-bit CAN address must be between 0x000 and 0x7FF.");
        }

        if (address.Extended29Bit &&
            (address.TxCanId > 0x1FFFFFFF ||
             address.RxCanId > 0x1FFFFFFF))
        {
            throw new ArgumentOutOfRangeException(
                nameof(address),
                "29-bit CAN address must be between 0x00000000 and 0x1FFFFFFF.");
        }

        var identity = string.IsNullOrWhiteSpace(address.DefinitionId)
            ? $"legacy:{address.Source}"
            : address.DefinitionId;
        _entries[$"{address.Brand}|{address.VehicleProfile}|{address.EcuId}|{identity}"] = address;
    }

    public bool TryGet(string brand, string vehicleProfile, string ecuId, out VerifiedEcuAddress address)
    {
        address = _entries.Values.FirstOrDefault(item =>
            item.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase) &&
            item.VehicleProfile.Equals(vehicleProfile, StringComparison.OrdinalIgnoreCase) &&
            item.EcuId.Equals(ecuId, StringComparison.OrdinalIgnoreCase))!;
        return address is not null;
    }

    public IReadOnlyList<VerifiedEcuAddress> GetForVehicle(ResolvedVehicleProfile profile)
    {
        if (!profile.CanUseManufacturerSpecificDefinitions)
            return Array.Empty<VerifiedEcuAddress>();

        var vehicle = profile.DiagnosticVehicle;
        return _entries.Values
            .Where(address => MatchesVerifiedBrand(address.Brand, profile))
            .Where(address => MatchesVerifiedVehicleProfile(address.VehicleProfile, profile))
            .Where(address => MatchesVerifiedYear(address, profile))
            .ToArray();
    }

    private static bool MatchesVerifiedBrand(string definitionBrand, ResolvedVehicleProfile vehicle)
    {
        var detectedBrand = vehicle.DiagnosticVehicle.Manufacturer;
        if (definitionBrand.Contains(detectedBrand, StringComparison.OrdinalIgnoreCase) ||
            detectedBrand.Contains(definitionBrand, StringComparison.OrdinalIgnoreCase))
            return true;

        return !vehicle.SelectedBrand.Id.Equals("other", StringComparison.OrdinalIgnoreCase) &&
               VehicleProfileResolutionEngine.MatchesBrand(vehicle.SelectedBrand, definitionBrand);
    }

    private static bool MatchesVerifiedVehicleProfile(
        string definitionProfile,
        ResolvedVehicleProfile vehicle)
    {
        if (definitionProfile == "*")
            return true;
        if (!vehicle.HasVerifiedModel)
            return false;
        if (definitionProfile.Equals(vehicle.Model.Value, StringComparison.OrdinalIgnoreCase))
            return true;
        return vehicle.HasVerifiedYear &&
               definitionProfile.Equals(
                   $"{vehicle.Model.Value} {vehicle.Year.Value}",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesVerifiedYear(VerifiedEcuAddress address, ResolvedVehicleProfile vehicle)
    {
        if (!address.MinimumYear.HasValue && !address.MaximumYear.HasValue)
            return true;
        if (!vehicle.HasVerifiedYear || !int.TryParse(vehicle.Year.Value, out var year))
            return false;
        return (!address.MinimumYear.HasValue || year >= address.MinimumYear) &&
               (!address.MaximumYear.HasValue || year <= address.MaximumYear);
    }

    public IReadOnlyList<VerifiedEcuAddress> All => _entries.Values.ToArray();
}
