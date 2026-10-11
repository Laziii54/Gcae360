namespace CarScanner.Services.Coding;

/// <summary>One readable value of a module (identification or coding block).</summary>
public sealed class CodingDidValue
{
    public string Did { get; set; } = string.Empty;
    /// <summary>Data bytes after "62 DID" as hex, or empty when not readable.</summary>
    public string Hex { get; set; } = string.Empty;
    /// <summary>Negative response code (e.g. 31 = not supported) or "timeout".</summary>
    public string Error { get; set; } = string.Empty;
    public bool IsRead => Hex.Length > 0;
}

/// <summary>Everything read from one module during a backup.</summary>
public sealed class CodingModuleBackup
{
    public string ModuleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Tx { get; set; }
    public int Rx { get; set; }
    public bool Extended29Bit { get; set; }
    public byte? ExtendedAddress { get; set; }

    /// <summary>
    /// How the read of this module ended: "read" (values stored), "refused" (the module answered every request with a
    /// refusal), "foreign" (only another module's frames arrived), "silent" (no answer). Empty in older backups.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Plain-language note about the read (e.g. which module actually answered); empty when nothing needs saying.</summary>
    public string Note { get; set; } = string.Empty;

    public string PartNumber { get; set; } = string.Empty;
    public string SoftwareVersion { get; set; } = string.Empty;
    public List<CodingDidValue> Values { get; set; } = new();
}

/// <summary>A full read-only backup of all answering modules.</summary>
public sealed class CodingBackup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Vin { get; set; } = string.Empty;
    public string Vehicle { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    /// <summary>Things the person should know about this backup (modules that did not answer, foreign replies …).</summary>
    public List<string> Warnings { get; set; } = new();

    public List<CodingModuleBackup> Modules { get; set; } = new();
    public int ReadCount => Modules.Sum(m => m.Values.Count(v => v.IsRead));
}

/// <summary>A byte that differs between two backups.</summary>
public sealed record CodingDifference(string Module, string Did, int ByteIndex, string Before, string After, string BitsChanged);

public sealed class CodingValueChoice
{
    public string Raw { get; set; } = string.Empty;
    public string Ka { get; set; } = string.Empty;
    public string En { get; set; } = string.Empty;
}

/// <summary>A verified coding option from coding_catalog.json.</summary>
public sealed class CodingOption
{
    public string Id { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public List<string> Models { get; set; } = new();
    public int YearFrom { get; set; }
    public int YearTo { get; set; } = 9999;
    public string ModuleTx { get; set; } = string.Empty;
    public string ModuleRx { get; set; } = string.Empty;
    public List<string> PartNumbers { get; set; } = new();
    public string Did { get; set; } = string.Empty;
    public int ByteIndex { get; set; }
    public string Mask { get; set; } = "FF";
    public List<CodingValueChoice> Values { get; set; } = new();
    /// <summary>Diagnostic session for writing (usually "03").</summary>
    public string Session { get; set; } = "03";
    public bool RequiresEngineOff { get; set; } = true;
    public bool RequiresSecurityAccess { get; set; }
    public string TitleKa { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
    public string DescriptionKa { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    /// <summary>"verified" options are shown; anything else stays hidden.</summary>
    public string Status { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;

    public int TxId => int.TryParse(ModuleTx, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    public int RxId => int.TryParse(ModuleRx, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    public int DidId => int.TryParse(Did, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : -1;
    public byte MaskByte => byte.TryParse(Mask, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : (byte)0xFF;
}

/// <summary>One change made by the app (kept for "Undo").</summary>
public sealed class CodingChange
{
    public DateTime At { get; set; } = DateTime.Now;
    public string Vin { get; set; } = string.Empty;
    public string OptionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Tx { get; set; }
    public int Rx { get; set; }
    public string Did { get; set; } = string.Empty;
    public string BeforeHex { get; set; } = string.Empty;
    public string AfterHex { get; set; } = string.Empty;
    public bool Restored { get; set; }
}

public sealed record CodingResult(bool Success, string Message);
