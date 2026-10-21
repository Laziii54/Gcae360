using CarScanner.Services;

namespace CarScanner.Services;

/// <summary>
/// The test project's half of the partial class <c>HybridHealthService</c>. The real Chevrolet Volt / Bolt reader
/// (HybridHealthService.Gm.cs: <c>ReadGmPackAsync</c>, the 96 cell requests, the thresholds, the messages it logs) and the
/// shared session helper (HybridHealthService.Pack.cs: <c>AskAsync</c>) are linked into this project UNCHANGED, so the tests run
/// the production code. The main file (profiles, the other makes, <c>ModuleLiveDataService</c>, the view-model glue) depends on
/// the app and is not linked; this shim supplies the only things the two linked files take from it: the OBD service, and
/// two label-text helpers, <c>Shorten</c> and <c>Rating</c> (copies of the main file's; they only shorten log text and name a score).
/// </summary>
public sealed partial class HybridHealthService
{
    private readonly OBDService _obd;

    public HybridHealthService(OBDService obd) => _obd = obd;

    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private static string Rating(int? score) => score switch
    {
        null => "ვერ შეფასდა",
        >= 80 => "კარგია",
        >= 60 => "საჭიროებს ყურადღებას",
        _ => "შესამოწმებელია"
    };
}
