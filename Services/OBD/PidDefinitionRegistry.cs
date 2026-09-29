using CarScanner.Models;

namespace CarScanner.Services.OBD;

public static class PidDefinitionRegistry
{
    private static readonly IReadOnlyDictionary<int, PidDefinition> Definitions =
        new Dictionary<int, PidDefinition>
        {
            [0x04] = new(0x04, "Calculated engine load", "%", "Engine", 0, 100, "100*A/255", "Calculated engine load."),
            [0x05] = new(0x05, "Engine coolant temperature", "°C", "Engine", -40, 215, "A-40", "Engine coolant temperature."),
            [0x06] = new(0x06, "Short-term fuel trim bank 1", "%", "Fuel", -100, 99.2, "100*A/128-100", "Short-term fuel trim bank 1."),
            [0x07] = new(0x07, "Long-term fuel trim bank 1", "%", "Fuel", -100, 99.2, "100*A/128-100", "Long-term fuel trim bank 1."),
            [0x08] = new(0x08, "Short-term fuel trim bank 2", "%", "Fuel", -100, 99.2, "100*A/128-100", "Short-term fuel trim bank 2."),
            [0x09] = new(0x09, "Long-term fuel trim bank 2", "%", "Fuel", -100, 99.2, "100*A/128-100", "Long-term fuel trim bank 2."),
            [0x0A] = new(0x0A, "Fuel pressure", "kPa", "Fuel", 0, 765, "3*A", "Fuel pressure."),
            [0x0B] = new(0x0B, "Intake manifold pressure", "kPa", "Air", 0, 255, "A", "Intake manifold absolute pressure."),
            [0x0C] = new(0x0C, "Engine RPM", "rpm", "Engine", 0, 16383.75, "((256*A)+B)/4", "Engine speed."),
            [0x0D] = new(0x0D, "Vehicle speed", "km/h", "Vehicle", 0, 255, "A", "Vehicle speed."),
            [0x0E] = new(0x0E, "Timing advance", "°", "Engine", -64, 63.5, "A/2-64", "Ignition timing advance."),
            [0x0F] = new(0x0F, "Intake air temperature", "°C", "Air", -40, 215, "A-40", "Intake air temperature."),
            [0x10] = new(0x10, "MAF air flow", "g/s", "Air", 0, 655.35, "((256*A)+B)/100", "Mass air flow."),
            [0x11] = new(0x11, "Throttle position", "%", "Engine", 0, 100, "100*A/255", "Throttle position."),

            [0x21] = new(0x21, "Distance with MIL on", "km", "Diagnostics", 0, 65535, "(256*A)+B", "Distance traveled while MIL was activated."),
            [0x2E] = new(0x2E, "Commanded evaporative purge", "%", "EVAP", 0, 100, "100*A/255", "Commanded evaporative purge."),
            [0x2F] = new(0x2F, "Fuel tank level", "%", "Fuel", 0, 100, "100*A/255", "Fuel tank level."),
            [0x30] = new(0x30, "Warm-ups since DTC clear", "count", "Diagnostics", 0, 255, "A", "Warm-up cycles since DTCs were cleared."),
            [0x31] = new(0x31, "Distance since DTC clear", "km", "Diagnostics", 0, 65535, "(256*A)+B", "Distance since diagnostic trouble codes were cleared."),
            [0x33] = new(0x33, "Barometric pressure", "kPa", "Air", 0, 255, "A", "Barometric pressure."),

            [0x42] = new(0x42, "Control module voltage", "V", "Electrical", 0, 65.535, "((256*A)+B)/1000", "ECU supply/control module voltage."),
            [0x46] = new(0x46, "Ambient air temperature", "°C", "Air", -40, 215, "A-40", "Ambient air temperature."),
            [0x4C] = new(0x4C, "Commanded throttle actuator", "%", "Engine", 0, 100, "100*A/255", "Commanded throttle actuator."),
            [0x4D] = new(0x4D, "Time run since engine start", "s", "Engine", 0, 65535, "(256*A)+B", "Time since engine start."),
            [0x4E] = new(0x4E, "Time since DTC clear", "min", "Diagnostics", 0, 65535, "(256*A)+B", "Time since diagnostic trouble codes were cleared."),

            [0x52] = new(0x52, "Ethanol fuel percentage", "%", "Fuel", 0, 100, "100*A/255", "Ethanol fuel percentage where supported."),
            [0x5B] = new(0x5B, "Hybrid battery remaining life", "%", "Hybrid", 0, 100, "100*A/255", "Generic hybrid battery remaining life where exposed."),
            [0x5C] = new(0x5C, "Engine oil temperature", "°C", "Engine", -40, 210, "A-40", "Engine oil temperature."),
            [0x5E] = new(0x5E, "Engine fuel rate", "L/h", "Fuel", 0, 3212.75, "((256*A)+B)/20", "Engine fuel rate where supported."),
            [0x61] = new(0x61, "Driver demanded engine torque", "%", "Engine", -125, 100, "A-125", "Driver demanded engine torque percentage where supported."),
            [0x62] = new(0x62, "Actual engine torque", "%", "Engine", -125, 100, "A-125", "Actual engine torque percentage where supported."),
            [0x63] = new(0x63, "Reference engine torque", "Nm", "Engine", 0, 65535, "(256*A)+B", "Reference engine torque where supported.")
        };

    // Convert the dictionary values to an actual IReadOnlyCollection.
    public static IReadOnlyCollection<PidDefinition> All =>
        Definitions.Values.ToArray();

    public static bool TryGet(
        int pid,
        out PidDefinition definition)
    {
        return Definitions.TryGetValue(
            pid,
            out definition!);
    }
}