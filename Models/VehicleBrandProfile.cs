namespace CarScanner.Models;

/// <summary>
/// A user-selected vehicle profile.  It controls the app experience and is
/// deliberately separate from VIN-confirmed vehicle information.
/// </summary>
public sealed record VehicleBrandProfile(
    string Id,
    string DisplayName,
    string Mark,
    string AccentColor,
    string GenericSupportNote);
