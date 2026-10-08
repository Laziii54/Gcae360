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
        // Standard J1979 PIDs without a decoder yet: shown with their raw data.
        if (!d.ContainsKey(0x5F)) Add(0x5F,"Emission requirements","","სხვა");
        if (!d.ContainsKey(0x64)) Add(0x64,"Engine percent torque data","","სხვა");
        if (!d.ContainsKey(0x65)) Add(0x65,"Auxiliary input / output","","სხვა");
        if (!d.ContainsKey(0x69)) Add(0x69,"Commanded EGR and EGR error","","სხვა");
        if (!d.ContainsKey(0x6A)) Add(0x6A,"Commanded diesel intake air flow","","სხვა");
        if (!d.ContainsKey(0x6B)) Add(0x6B,"EGR temperature","","სხვა");
        if (!d.ContainsKey(0x6C)) Add(0x6C,"Commanded throttle actuator and position","","სხვა");
        if (!d.ContainsKey(0x6D)) Add(0x6D,"Fuel pressure control system","","სხვა");
        if (!d.ContainsKey(0x6E)) Add(0x6E,"Injection pressure control system","","სხვა");
        if (!d.ContainsKey(0x6F)) Add(0x6F,"Turbocharger compressor inlet pressure","","სხვა");
        if (!d.ContainsKey(0x70)) Add(0x70,"Boost pressure control","","სხვა");
        if (!d.ContainsKey(0x71)) Add(0x71,"Variable geometry turbo control","","სხვა");
        if (!d.ContainsKey(0x72)) Add(0x72,"Wastegate control","","სხვა");
        if (!d.ContainsKey(0x74)) Add(0x74,"Turbocharger RPM","","სხვა");
        if (!d.ContainsKey(0x75)) Add(0x75,"Turbocharger temperature A","","სხვა");
        if (!d.ContainsKey(0x76)) Add(0x76,"Turbocharger temperature B","","სხვა");
        if (!d.ContainsKey(0x7A)) Add(0x7A,"DPF bank 1","","სხვა");
        if (!d.ContainsKey(0x7B)) Add(0x7B,"DPF bank 2","","სხვა");
        if (!d.ContainsKey(0x7D)) Add(0x7D,"NOx NTE control area status","","სხვა");
        if (!d.ContainsKey(0x7E)) Add(0x7E,"PM NTE control area status","","სხვა");
        if (!d.ContainsKey(0x81)) Add(0x81,"Engine run time for AECD 1","","სხვა");
        if (!d.ContainsKey(0x82)) Add(0x82,"Engine run time for AECD 2","","სხვა");
        if (!d.ContainsKey(0x83)) Add(0x83,"NOx sensor","","სხვა");
        if (!d.ContainsKey(0x85)) Add(0x85,"NOx reagent system","","სხვა");
        if (!d.ContainsKey(0x86)) Add(0x86,"Particulate matter sensor","","სხვა");
        if (!d.ContainsKey(0x87)) Add(0x87,"Intake manifold absolute pressure","","სხვა");
        if (!d.ContainsKey(0x88)) Add(0x88,"SCR induce system","","სხვა");
        if (!d.ContainsKey(0x89)) Add(0x89,"Run time for AECD 11-15","","სხვა");
        if (!d.ContainsKey(0x8A)) Add(0x8A,"Run time for AECD 16-20","","სხვა");
        if (!d.ContainsKey(0x8B)) Add(0x8B,"Diesel aftertreatment","","სხვა");
        if (!d.ContainsKey(0x8C)) Add(0x8C,"O2 sensor (wide range)","","სხვა");
        if (!d.ContainsKey(0x8F)) Add(0x8F,"PM sensor output","","სხვა");
        if (!d.ContainsKey(0x90)) Add(0x90,"WWH-OBD vehicle information","","სხვა");
        if (!d.ContainsKey(0x91)) Add(0x91,"WWH-OBD ECU information","","სხვა");
        if (!d.ContainsKey(0x92)) Add(0x92,"Fuel system control","","სხვა");
        if (!d.ContainsKey(0x93)) Add(0x93,"WWH-OBD counters","","სხვა");
        if (!d.ContainsKey(0x94)) Add(0x94,"NOx warning and inducement","","სხვა");
        if (!d.ContainsKey(0x98)) Add(0x98,"Exhaust gas temperature sensor","","სხვა");
        if (!d.ContainsKey(0x99)) Add(0x99,"Exhaust gas temperature sensor 2","","სხვა");
        if (!d.ContainsKey(0x9A)) Add(0x9A,"Hybrid/EV system data (battery voltage)","","სხვა");
        if (!d.ContainsKey(0x9B)) Add(0x9B,"Diesel exhaust fluid sensor","","სხვა");
        if (!d.ContainsKey(0x9C)) Add(0x9C,"O2 sensor data","","სხვა");
        if (!d.ContainsKey(0x9F)) Add(0x9F,"Fuel system percentage use","","სხვა");
        if (!d.ContainsKey(0xA1)) Add(0xA1,"NOx sensor corrected data","","სხვა");
        if (!d.ContainsKey(0xA3)) Add(0xA3,"Evap system vapor pressure","","სხვა");
        if (!d.ContainsKey(0xA5)) Add(0xA5,"DEF dosing","","სხვა");
        if (!d.ContainsKey(0xA7)) Add(0xA7,"NOx sensor 2","","სხვა");
        if (!d.ContainsKey(0xA8)) Add(0xA8,"NOx sensor 2 corrected","","სხვა");
        if (!d.ContainsKey(0xA9)) Add(0xA9,"ABS disable switch state","","სხვა");
        if (!d.ContainsKey(0xC3)) Add(0xC3,"Fuel level input A/B","","სხვა");
        if (!d.ContainsKey(0xC4)) Add(0xC4,"Exhaust particulate control diagnostic time/count","","სხვა");
        if (!d.ContainsKey(0xC5)) Add(0xC5,"Fuel pressure A and B","","სხვა");
        if (!d.ContainsKey(0xC6)) Add(0xC6,"Particulate control status counters","","სხვა");
        if (!d.ContainsKey(0xC7)) Add(0xC7,"Distance since reflash","","სხვა");
        if (!d.ContainsKey(0xC8)) Add(0xC8,"NOx control diagnostic","","სხვა");
        return d;
    }
    public static SupportedPid? Get(int pid) => Items.TryGetValue(pid, out var item) ? item : null;
    public static IEnumerable<SupportedPid> All => Items.Values.OrderBy(x=>x.Pid);
}
