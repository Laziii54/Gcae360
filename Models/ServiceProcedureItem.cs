namespace CarScanner.Models;

/// <summary>
/// ერთი Service/Coding ელემენტი, რომელსაც UI აჩვენებს.
///
/// შეუძლია იყოს:
/// 1) რეალური, დადასტურებული OEM ოპერაცია;
/// 2) კონკრეტული მანქანისთვის დადასტურებული manual/guided პროცედურა.
///
/// Manual procedure არასოდეს აგზავნის ECU write ბრძანებას.
/// </summary>
public sealed class ServiceProcedureItem
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Group { get; init; } = "";

    public string Description { get; init; } = "";

    /// <summary>
    /// oem_operations.json-ში არსებული OperationId.
    /// Manual procedure-ისთვის ცარიელია.
    /// </summary>
    public string OperationId { get; init; } = "";

    /// <summary>
    /// true = მომხმარებელს ინსტრუქცია ეჩვენება
    /// და ECU-ში ბრძანება არ იგზავნება.
    /// </summary>
    public bool IsManualProcedure { get; init; }

    /// <summary>
    /// Localized manual instructions.
    /// </summary>
    public string ManualInstructions { get; init; } = "";

    /// <summary>Text on the item's action button (RESET or GUIDE).</summary>
    public string ButtonText { get; init; } = "RESET";

    public bool IsActiveTest { get; init; }

    public bool IsAvailable { get; init; }

    public bool RequiresVerifiedOemProfile { get; init; } = true;

    public string AvailabilityOverride { get; init; } = "";

    public string AvailabilityOverrideColor { get; init; } = "";

    /// <summary>
    /// true მხოლოდ მაშინ, როცა ეს რეალური OEM operation-ია.
    /// </summary>
    public bool IsOemOperation =>
        !IsManualProcedure &&
        !string.IsNullOrWhiteSpace(OperationId);

    public string AvailabilityText =>
        !string.IsNullOrWhiteSpace(AvailabilityOverride)
            ? AvailabilityOverride
            : IsManualProcedure
                ? "GUIDED PROCEDURE"
                : RequiresVerifiedOemProfile
                    ? "NOT SUPPORTED · OEM დიაგნოსტიკა საჭიროა"
                    : "AVAILABLE · Generic OBD-II";

    public string AvailabilityColor =>
        !string.IsNullOrWhiteSpace(AvailabilityOverrideColor)
            ? AvailabilityOverrideColor
            : IsManualProcedure
                ? "#60A5FA"
                : RequiresVerifiedOemProfile
                    ? "#F59E0B"
                    : "#4ADE80";
}