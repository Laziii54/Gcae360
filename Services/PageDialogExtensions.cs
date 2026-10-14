using CarScanner.Services.Localization;

namespace CarScanner.Services;

/// <summary>
/// Alerts, action sheets and prompts are native dialogs: they are not part of the page tree, so the label translation
/// hook never sees them. For every language except Georgian and English the view models hand over the English text, and
/// a plain <c>DisplayAlertAsync</c> showed it untranslated. These wrappers translate every visible string with the
/// current language first; an action sheet maps the tapped (translated) button back to the text the caller passed in.
/// </summary>
public static class PageDialogExtensions
{
    private static string T(string? text) => string.IsNullOrEmpty(text) ? text ?? string.Empty : LocalizationService.T(text);

    public static Task DisplayAlertLocalizedAsync(this Page page, string title, string message, string cancel) =>
        page.DisplayAlertAsync(T(title), T(message), T(cancel));

    /// <summary>A null <paramref name="cancel"/> hides the second button entirely (MAUI behaviour), not just its label.</summary>
    public static Task<bool> DisplayAlertLocalizedAsync(this Page page, string title, string message, string accept, string? cancel) =>
        page.DisplayAlertAsync(T(title), T(message), T(accept), cancel is null ? null : T(cancel));

    /// <summary>A null <paramref name="cancel"/> hides the second button entirely (MAUI behaviour), not just its label.</summary>
    public static Task<string?> DisplayPromptLocalizedAsync(
        this Page page,
        string title,
        string message,
        string accept = "OK",
        string? cancel = "Cancel",
        string? placeholder = null,
        int maxLength = -1,
        Keyboard? keyboard = null,
        string initialValue = "") =>
        page.DisplayPromptAsync(T(title), T(message), T(accept), cancel is null ? null : T(cancel), placeholder is null ? null : T(placeholder), maxLength, keyboard, initialValue);

    /// <summary>Returns the button text exactly as the caller passed it (not the translated text that was shown).</summary>
    public static async Task<string?> DisplayActionSheetLocalizedAsync(
        this Page page,
        string title,
        string? cancel,
        string? destruction,
        params string[] buttons)
    {
        var shown = buttons.Select(T).ToArray();
        var translatedCancel = cancel is null ? null : T(cancel);
        var translatedDestruction = destruction is null ? null : T(destruction);

        var picked = await page.DisplayActionSheetAsync(T(title), translatedCancel, translatedDestruction, shown);
        if (picked is null)
            return null;
        if (translatedCancel is not null && picked == translatedCancel)
            return cancel;
        if (translatedDestruction is not null && picked == translatedDestruction)
            return destruction;

        var index = Array.IndexOf(shown, picked);
        return index >= 0 ? buttons[index] : picked;
    }
}
