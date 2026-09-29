namespace CarScanner.Models;
public class LiveDataModel
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int Rpm { get; set; } public bool HasRpm { get; set; }
    public int CoolantTemp { get; set; } public bool HasCoolantTemp { get; set; }
    public int Speed { get; set; } public bool HasSpeed { get; set; }
    public double Voltage { get; set; } public bool HasVoltage { get; set; }
    public int ThrottlePosition { get; set; } public bool HasThrottlePosition { get; set; }
    public int FuelLevel { get; set; } public bool HasFuelLevel { get; set; }
    public int EngineLoad { get; set; } public bool HasEngineLoad { get; set; }
    public double MapKpa { get; set; } public bool HasMap { get; set; }
    public double MafGs { get; set; } public bool HasMaf { get; set; }
    public double IntakeAirTemp { get; set; } public bool HasIntakeAirTemp { get; set; }
    public double AmbientTemp { get; set; } public bool HasAmbientTemp { get; set; }
    public double ShortFuelTrim { get; set; } public bool HasShortFuelTrim { get; set; }
    public double LongFuelTrim { get; set; } public bool HasLongFuelTrim { get; set; }
    public int FuelTypeCode { get; set; } public bool HasFuelType { get; set; }
    public string FuelTypeDescription => FuelTypeCode switch { 1=>"ბენზინი", 2=>"მეთანოლი", 3=>"ეთანოლი", 4=>"დიზელი", 5=>"LPG", 6=>"CNG", 7=>"პროპანი", 8=>"ელექტრო/ჰიბრიდი", _=>"უცნობი" };
}
