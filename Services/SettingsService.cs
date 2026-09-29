using CarScanner.Database;
using Microsoft.Maui.Storage;
namespace CarScanner.Services;
public sealed class SettingsService
{
    private readonly AppDatabase _db;
    public SettingsService(AppDatabase db)=>_db=db;
    public async Task<string> GetAsync(string key,string fallback="")=>await _db.GetSettingAsync(key) ?? Preferences.Default.Get(key,fallback);
    public async Task SetAsync(string key,string value){Preferences.Default.Set(key,value);await _db.SaveSettingAsync(key,value);}
    public Task<bool> GetBoolAsync(string key,bool fallback=false)=>Task.FromResult(Preferences.Default.Get(key,fallback));
    public async Task SetBoolAsync(string key,bool value){Preferences.Default.Set(key,value);await _db.SaveSettingAsync(key,value.ToString());}
}
