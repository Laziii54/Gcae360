using CarScanner.Models;

namespace CarScanner.Services.OBD;

public static class PidRegistry
{
    private static readonly IReadOnlyDictionary<int, SupportedPid> Items = Build();
    private static IReadOnlyDictionary<int, SupportedPid> Build()
    {
        var d = new Dictionary<int, SupportedPid>();
        void Add(int id, string name, string unit, string category) => d[id] = new SupportedPid { Pid = id, Name = name, Unit = unit, Category = category };
        Add(0x01,"Monitor status since DTCs cleared","","დიაგნოსტიკა"); Add(0x02,"Freeze-frame DTC","","დიაგნოსტიკა"); Add(0x03,"Fuel system status","","საწვავი");
        Add(0x04,"Calculated engine load","%","ძრავა"); Add(0x05,"Engine coolant temperature","°C","ძრავა");
        for(int p=0x06;p<=0x09;p++) Add(p,$"Fuel trim bank {(p%2==0?1:2)} {(p<=0x07?"short":"long")}","%","საწვავი");
        Add(0x0A,"Fuel pressure","kPa","საწვავი"); Add(0x0B,"Intake manifold pressure","kPa","ჰაერი"); Add(0x0C,"Engine RPM","rpm","ძრავა"); Add(0x0D,"Vehicle speed","km/h","ძრავა"); Add(0x0E,"Timing advance","°","ძრავა"); Add(0x0F,"Intake air temperature","°C","ჰაერი"); Add(0x10,"MAF air flow","g/s","ჰაერი"); Add(0x11,"Throttle position","%","ძრავა");
        Add(0x12,"Secondary air status","","ემისიები"); Add(0x13,"Oxygen sensors present","","ემისიები");
        for(int p=0x14;p<=0x1B;p++) Add(p,$"O2 sensor {p-0x13} voltage","V","ემისიები");
        Add(0x1C,"OBD standards supported","","დიაგნოსტიკა"); Add(0x1D,"O2 sensor locations","","ემისიები"); Add(0x1E,"Auxiliary input status","","ელექტრო"); Add(0x1F,"Run time since engine start","s","ძრავა");
        Add(0x21,"Distance with MIL on","km","დიაგნოსტიკა"); Add(0x23,"Fuel rail pressure","kPa","საწვავი"); Add(0x2C,"Commanded EGR","%","ემისიები"); Add(0x2D,"EGR error","%","ემისიები"); Add(0x2E,"Commanded evaporative purge","%","ემისიები"); Add(0x2F,"Fuel tank level","%","საწვავი"); Add(0x30,"Warm-ups since DTC clear","count","დიაგნოსტიკა"); Add(0x31,"Distance since DTC clear","km","დიაგნოსტიკა"); Add(0x32,"EVAP vapor pressure","Pa","ემისიები"); Add(0x33,"Barometric pressure","kPa","ჰაერი");
        Add(0x22,"Fuel rail pressure (relative)","kPa","საწვავი");
        for(int p=0x24;p<=0x2B;p++) Add(p,$"O2 sensor {p-0x23} lambda","ratio","ემისიები");
        for(int p=0x34;p<=0x3B;p++) Add(p,$"O2 sensor {p-0x33} lambda (wide-band)","ratio","ემისიები");
        Add(0x3C,"Catalyst temperature B1S1","°C","ემისიები"); Add(0x3D,"Catalyst temperature B2S1","°C","ემისიები"); Add(0x3E,"Catalyst temperature B1S2","°C","ემისიები"); Add(0x3F,"Catalyst temperature B2S2","°C","ემისიები");
        Add(0x42,"Control module voltage","V","ელექტრო"); Add(0x43,"Absolute load","%","ძრავა"); Add(0x44,"Commanded equivalence ratio","ratio","საწვავი"); Add(0x45,"Relative throttle position","%","ძრავა"); Add(0x46,"Ambient air temperature","°C","ჰაერი");
        Add(0x47,"Absolute throttle position B","%","ძრავა"); Add(0x48,"Absolute throttle position C","%","ძრავა"); Add(0x49,"Accelerator pedal position D","%","ძრავა"); Add(0x4A,"Accelerator pedal position E","%","ძრავა"); Add(0x4B,"Accelerator pedal position F","%","ძრავა"); Add(0x4C,"Commanded throttle actuator","%","ძრავა");
        Add(0x4D,"Time with MIL on","min","დიაგნოსტიკა"); Add(0x4E,"Time since DTC cleared","min","დიაგნოსტიკა"); Add(0x4F,"Maximum equivalence/O2 voltage","","ემისიები"); Add(0x50,"Maximum MAF","g/s","ჰაერი"); Add(0x51,"Fuel type","","საწვავი"); Add(0x52,"Ethanol fuel percentage","%","საწვავი"); Add(0x53,"Absolute EVAP vapor pressure","kPa","ემისიები"); Add(0x54,"EVAP vapor pressure","Pa","ემისიები");
        Add(0x55,"Secondary O2 short trim bank 1","%","ემისიები"); Add(0x56,"Secondary O2 long trim bank 1","%","ემისიები"); Add(0x57,"Secondary O2 short trim bank 2","%","ემისიები"); Add(0x58,"Secondary O2 long trim bank 2","%","ემისიები"); Add(0x59,"Fuel rail absolute pressure","kPa","საწვავი");
        Add(0x5A,"Relative accelerator pedal position","%","ძრავა"); Add(0x5B,"Hybrid battery remaining life","%","ჰიბრიდი"); Add(0x5C,"Engine oil temperature","°C","ძრავა"); Add(0x5D,"Fuel injection timing","°","საწვავი"); Add(0x5E,"Engine fuel rate","L/h","საწვავი"); Add(0x5F,"Fuel system design current","A","საწვავი");
        Add(0x61,"Driver demand engine torque","%","ძრავა"); Add(0x62,"Actual engine torque","%","ძრავა"); Add(0x63,"Engine reference torque","Nm","ძრავა");
        Add(0x66,"MAF sensor A","g/s","ჰაერი"); Add(0x67,"Engine coolant temperature sensor 1","°C","ძრავა"); Add(0x68,"Intake air temperature sensor 1","°C","ჰაერი");
        Add(0x73,"Exhaust pressure bank 1","kPa","ემისიები"); Add(0x77,"Charge air cooler temperature","°C","ჰაერი"); Add(0x78,"Exhaust gas temperature bank 1","°C","ემისიები"); Add(0x79,"Exhaust gas temperature bank 2","°C","ემისიები");
        Add(0x7C,"Diesel particulate filter temperature","°C","ემისიები"); Add(0x7F,"Total engine run time","h","ძრავა"); Add(0x84,"Manifold surface temperature","°C","ჰაერი");
        Add(0x8D,"Throttle position G","%","ძრავა"); Add(0x8E,"Engine friction torque","%","ძრავა"); Add(0x9D,"Engine fuel rate","g/s","საწვავი"); Add(0x9E,"Exhaust flow rate","kg/h","ემისიები");
        Add(0xA2,"Cylinder fuel rate","mg/stroke","საწვავი"); Add(0xA4,"Transmission actual gear","","მანქანა");
        Add(0xA6,"Standard odometer","km","მანქანა");
        return d;
    }
    public static SupportedPid? Get(int pid) => Items.TryGetValue(pid, out var item) ? item : null;
    public static IEnumerable<SupportedPid> All => Items.Values.OrderBy(x=>x.Pid);
}
