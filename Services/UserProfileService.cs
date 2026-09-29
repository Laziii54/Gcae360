using Microsoft.Maui.Storage;

namespace CarScanner.Services;

public sealed class UserProfileService
{
    private const string NameKey = "user_name";
    public string Name => Preferences.Default.Get(NameKey, string.Empty);
    public bool HasName => !string.IsNullOrWhiteSpace(Name);
    public void SetName(string name) => Preferences.Default.Set(NameKey, name.Trim());
}
