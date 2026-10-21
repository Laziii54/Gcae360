namespace CarScanner.Services.OemDiagnostics;

/// <summary>
/// Public definitions whose recorded replies show that they hold something other than what their name says. A request is listed here
/// only on evidence: the values OBDb's own tests recorded for it. Pure: no I/O.
///
/// The Service page never shows a value from one of these as what the name says: showing "distance since the last oil change" for a
/// number that is the odometer tells the owner something false, and a correct read would still be a wrong answer.
/// </summary>
public static class KnownMislabelledDefinitions
{
    public sealed record Entry(IReadOnlyList<string> Makes, int ServiceId, int DataIdentifier, string Name, string Evidence);

    private static readonly Entry[] Entries =
    {
        new(
            new[] { "BMW", "MINI", "Toyota" },
            0x22,
            0xDFE7,
            "Distance since last oil change",
            "OBDb names this value of 22 DFE7 (module 0x19) 'Distance since last oil change'. The replies recorded in OBDb's tests (BMW 3, 5, 7 Series, X3 and X5, " +
            "model years 2017-2024; 343 replies from 2,268 to 239,253 km) have the size of an odometer, not of a service interval: for the 3 Series 49 of 72 " +
            "recorded values lie within 50 km (or 0.2 %) of an odometer value recorded for the same model year, 74 of 134 for the 5 Series, 30 of 52 for the " +
            "7 Series, 51 of 83 for the X5. In one recorded 3 Series reply the value is 97,546 km next to an odometer of 97,545 km. MINI and the Toyota GR Supra " +
            "use the same definition; no reply is recorded for them.")
    };

    public static IReadOnlyList<Entry> All => Entries;

    /// <summary>True when the request is one of the listed definitions; <paramref name="evidence"/> then says why.</summary>
    public static bool TryFind(string? make, int serviceId, int dataIdentifier, string? name, out string evidence)
    {
        foreach (var entry in Entries)
        {
            if (entry.ServiceId == serviceId &&
                entry.DataIdentifier == dataIdentifier &&
                entry.Makes.Any(m => string.Equals(m, make, StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(entry.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                evidence = entry.Evidence;
                return true;
            }
        }

        evidence = string.Empty;
        return false;
    }
}
