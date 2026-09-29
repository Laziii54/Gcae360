namespace CarScanner.Models;

public sealed class DtcCatalogEntry
{
    // ============================================================
    // BASIC
    // ============================================================

    public string Code { get; set; } =
        string.Empty;

    public string Category { get; set; } =
        string.Empty;

    // SQLite master-catalog context.
    public string Scope { get; set; } =
        string.Empty;

    public string Manufacturer { get; set; } =
        string.Empty;

    public string EcuModel { get; set; } =
        string.Empty;

    public string EcuType { get; set; } =
        string.Empty;

    public string ResolutionStatus { get; set; } =
        string.Empty;

    /// <summary>
    /// True when the manufacturer context came from a VIN-confirmed vehicle.
    /// False can still mean the user's selected brand was used as a lookup hint.
    /// </summary>
    public bool ManufacturerContextVerified { get; set; }


    // ============================================================
    // ENGLISH
    // ============================================================

    public string TitleEn { get; set; } =
        string.Empty;

    public string DescriptionEn { get; set; } =
        string.Empty;

    public IReadOnlyList<string> CausesEn { get; set; } =
        Array.Empty<string>();

    public IReadOnlyList<string> SymptomsEn { get; set; } =
        Array.Empty<string>();


    // ============================================================
    // GEORGIAN
    // ============================================================

    public string TitleKa { get; set; } =
        string.Empty;

    public string DescriptionKa { get; set; } =
        string.Empty;

    public IReadOnlyList<string> CausesKa { get; set; } =
        Array.Empty<string>();

    public IReadOnlyList<string> SymptomsKa { get; set; } =
        Array.Empty<string>();

    /// <summary>What to check first (hand-written Georgian, generic codes only).</summary>
    public IReadOnlyList<string> ChecksKa { get; set; } =
        Array.Empty<string>();

    /// <summary>0 unknown, 1 low, 2 medium, 3 high.</summary>
    public int SeverityLevel { get; set; }

    /// <summary>True when the Georgian text comes from the hand-written set.</summary>
    public bool IsCurated { get; set; }


    // ============================================================
    // DIAGNOSTIC FLAGS
    // ============================================================

    public bool Mil { get; set; }

    public bool EmissionsRelevant { get; set; }

    public bool LimpMode { get; set; }


    // ============================================================
    // REPAIR INFORMATION
    // ============================================================

    public string Difficulty { get; set; } =
        string.Empty;


    // ============================================================
    // SOURCE
    // ============================================================

    public string Source { get; set; } =
        string.Empty;
}
