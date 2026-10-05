namespace CarScanner.Services.OBD;

/// <summary>
/// Current app language for OBD texts built outside view models (records,
/// definitions). Platform-independent: the app plugs in the real language at
/// startup (LocalizationService), unit tests keep the English default.
/// </summary>
public static class ObdLang
{
    /// <summary>Returns true when the app language is English. Set once at startup.</summary>
    public static Func<bool> IsEnglishProvider { get; set; } = static () => true;

    public static bool IsEnglish
    {
        get
        {
            try
            {
                return IsEnglishProvider();
            }
            catch
            {
                return true;
            }
        }
    }

    public static string L(string ka, string en) => IsEnglish ? en : ka;
}
