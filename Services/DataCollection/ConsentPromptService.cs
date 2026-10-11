using CarScanner.Services.Localization;
using CarScanner.Views;

namespace CarScanner.Services.DataCollection;

/// <summary>Preferences-backed storage for <see cref="ConsentPolicy"/> (survives restarts and reboots).</summary>
public sealed class PreferencesConsentStorage : IConsentStorage
{
    public bool GetBool(string key, bool fallback) => Preferences.Default.Get(key, fallback);
    public void SetBool(string key, bool value) => Preferences.Default.Set(key, value);
    public long GetLong(string key, long fallback) => Preferences.Default.Get(key, fallback);
    public void SetLong(string key, long value) => Preferences.Default.Set(key, value);
}

/// <summary>
/// Connects the consent question to the app: asks at launch and when the app returns to the foreground, only
/// while the question is open, never twice at once, and never over another modal or a recording trip. Saying
/// yes turns on the existing collection pipeline (<see cref="DataCollectionService"/>); nothing else happens.
/// </summary>
public sealed class ConsentPromptService
{
    private readonly ConsentPromptCoordinator _coordinator;

    public ConsentPromptService(DataCollectionService collection)
    {
        _coordinator = new ConsentPromptCoordinator(
            collection.Consent,
            CanShowNow,
            ShowAsync,
            () => collection.SetEnabledAsync(true));
    }

    /// <summary>Call after the main page is on screen and each time the app resumes. Never throws.</summary>
    public async Task CheckAsync()
    {
        try
        {
            // The coordinator is cheap when nothing is to be asked; the UI work must run on the main thread.
            await MainThread.InvokeOnMainThreadAsync(async () => await _coordinator.CheckAsync());
        }
        catch (Exception ex)
        {
            CommandErrors.Report(ex, "ConsentPromptService.CheckAsync");
        }
    }

    private static bool CanShowNow()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is not Shell shell || shell.CurrentPage is null)
            return false;                                   // still on the opening animation

        return shell.Navigation.ModalStack.Count == 0       // no other modal (e.g. the language picker)
            && !CarScanner.Services.Background.TripKeepAlive.IsActive;   // never over a recording trip
    }

    private static Task<ConsentChoice?> ShowAsync() =>
        ConsentPage.ShowAsync(ConsentTexts.For(LanguageCatalog.Current.Code));
}
