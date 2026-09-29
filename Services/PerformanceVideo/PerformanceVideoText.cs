using System.Globalization;
using System.Resources;
using CarScanner.Services.Localization;

namespace CarScanner.Services.PerformanceVideo;

/// <summary>
/// Performance Video texts. All strings live in
/// Resources/Strings/PerformanceVideoStrings.resx (English) and
/// Resources/Strings/PerformanceVideoStringsKa.resx (Georgian); the app's selected language
/// decides which one is used.
/// </summary>
public static class PerformanceVideoText
{
    public const string Title = "PV_Title";
    public const string OpenCamera = "PV_OpenCamera";
    public const string StartRecording = "PV_StartRecording";
    public const string StopRecording = "PV_StopRecording";
    public const string Rec = "PV_Rec";
    public const string Close = "PV_Close";
    public const string SaveTitle = "PV_SaveTitle";
    public const string SaveHint = "PV_SaveHint";
    public const string SaveWithData = "PV_SaveWithData";
    public const string SaveWithoutData = "PV_SaveWithoutData";
    public const string SaveBoth = "PV_SaveBoth";
    public const string Discard = "PV_Discard";
    public const string Processing = "PV_Processing";
    public const string Saving = "PV_Saving";
    public const string Saved = "PV_Saved";
    public const string SavedBoth = "PV_SavedBoth";
    public const string Discarded = "PV_Discarded";
    public const string SaveFailed = "PV_SaveFailed";
    public const string CameraPermission = "PV_CameraPermission";
    public const string StoragePermission = "PV_StoragePermission";
    public const string CameraError = "PV_CameraError";
    public const string RecordError = "PV_RecordError";
    public const string NotSupported = "PV_NotSupported";
    public const string WaitingSpeed = "PV_WaitingSpeed";
    public const string ReadyHint = "PV_ReadyHint";
    public const string Seconds = "PV_Seconds";

    private static readonly Lazy<ResourceManager?> English = new(() => CreateManager("PerformanceVideoStrings"));
    private static readonly Lazy<ResourceManager?> Georgian = new(() => CreateManager("PerformanceVideoStringsKa"));

    // Two separate resource files (no satellite/culture lookup), so the
    // texts work the same on every device and globalization mode.
    private static ResourceManager? CreateManager(string baseName)
    {
        var assembly = typeof(PerformanceVideoText).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("." + baseName + ".resources", StringComparison.Ordinal));
        return resource is null
            ? null
            : new ResourceManager(resource[..^".resources".Length], assembly);
    }

    public static bool IsEnglish =>
        Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>()?.IsEnglish ?? false;

    public static string Get(string key)
    {
        try
        {
            var manager = IsEnglish ? English.Value : Georgian.Value;
            return manager?.GetString(key, CultureInfo.InvariantCulture)
                ?? English.Value?.GetString(key, CultureInfo.InvariantCulture)
                ?? key;
        }
        catch
        {
            return key;
        }
    }

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), args);
}
