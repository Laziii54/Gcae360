namespace CarScanner.Models;

public enum VehicleProfileVerificationState
{
    Confirmed,
    SelectionMismatch,
    VinManufacturerUnresolved,
    SelectionOnly,
    Unavailable
}

public sealed record ResolvedVehicleField(
    string Value,
    VehicleDataOrigin Origin,
    bool IsVerified);

/// <summary>
/// One resolved identity for the active connection. DiagnosticVehicle always
/// contains detected/cache-backed data; user selection is retained separately
/// and can never masquerade as a VIN-confirmed manufacturer.
/// </summary>
public sealed class ResolvedVehicleProfile
{
    public VehicleInfo DiagnosticVehicle { get; init; } = new();
    public VehicleBrandProfile SelectedBrand { get; init; } =
        new("other", "სხვა მარკა", "OBD", "#22C55E", string.Empty);
    public VehicleProfileVerificationState VerificationState { get; init; }
    public ResolvedVehicleField VIN { get; init; } =
        new("მიუწვდომელია", VehicleDataOrigin.Unknown, false);
    public ResolvedVehicleField Manufacturer { get; init; } =
        new("მიუწვდომელია", VehicleDataOrigin.Unknown, false);
    public ResolvedVehicleField Model { get; init; } =
        new("მიუწვდომელია", VehicleDataOrigin.Unknown, false);
    public ResolvedVehicleField Year { get; init; } =
        new("მიუწვდომელია", VehicleDataOrigin.Unknown, false);
    public ResolvedVehicleField Engine { get; init; } =
        new("მიუწვდომელია", VehicleDataOrigin.Unknown, false);
    public ResolvedVehicleField FuelType { get; init; } =
        new("მიუწვდომელია", VehicleDataOrigin.Unknown, false);
    public ResolvedVehicleField Protocol { get; init; } =
        new("მიუწვდომელია", VehicleDataOrigin.Unknown, false);
    public string VerificationMessage { get; init; } = "ავტომობილი ჯერ არ არის ამოცნობილი.";
    public string Evidence { get; init; } = "ECU/VIN მონაცემი ჯერ არ არსებობს.";

    public bool HasValidVin => VIN.IsVerified;
    public bool HasVerifiedModel => Model.IsVerified;
    public bool HasVerifiedYear => Year.IsVerified;

    public bool CanUseManufacturerSpecificDefinitions =>
        VerificationState == VehicleProfileVerificationState.Confirmed &&
        VIN.IsVerified &&
        Manufacturer.IsVerified;

    public string DisplayName
    {
        get
        {
            var parts = new[] { Manufacturer.Value, Model.Value, Year.Value }
                .Where(value => !string.IsNullOrWhiteSpace(value) && value != "მიუწვდომელია")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return parts.Length == 0 ? SelectedBrand.DisplayName : string.Join(" ", parts);
        }
    }
}
