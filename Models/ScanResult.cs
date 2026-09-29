using SQLite;

namespace CarScanner.Models;

public class ScanResult
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public DateTime ScanDate { get; set; } = DateTime.Now;
    public string CarInfo { get; set; } = "უცნობი მანქანა";
    public string FoundCodes { get; set; } = string.Empty; // JSON ან მძიმით გამოყოფილი
    public string Notes { get; set; } = string.Empty;
}