using SQLite;
namespace CarScanner.Models;
public sealed class SettingItem
{
    [PrimaryKey] public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
