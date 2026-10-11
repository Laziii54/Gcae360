namespace CarScanner.Models;

/// <summary>Result of reading trouble codes from one vehicle module (ABS, airbag, body …).</summary>
public sealed class ModuleDtcReport
{
    public string ModuleId { get; set; } = string.Empty;
    public string NameKa { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public int TransmitCanId { get; set; }
    public int ReceiveCanId { get; set; }
    public bool Extended29Bit { get; set; }

    /// <summary>BMW-style CAN extended (target) address; null for normal addressing.</summary>
    public byte? ExtendedAddress { get; set; }

    /// <summary>Name reported by the module itself (e.g. "ABS ESP"), when available.</summary>
    public string EcuName { get; set; } = string.Empty;

    /// <summary>Software version / part number reported by the module, when available.</summary>
    public string SoftwareVersion { get; set; } = string.Empty;

    /// <summary>Identification read from the module (VIN, part number, versions, serial …).</summary>
    public List<ModuleSnapshotValue> Identity { get; set; } = new();

    /// <summary>UDS, KWP or GMLAN.</summary>
    public string Protocol { get; set; } = string.Empty;

    /// <summary>The module answered the trouble-code request.</summary>
    public bool Responded { get; set; }

    /// <summary>
    /// False when the module started a multi-frame answer that did not arrive completely: the codes shown are only
    /// those fully received, and the list may be longer.
    /// </summary>
    public bool CodeListComplete { get; set; } = true;

    /// <summary>Plain-language note about this reply (refusal code, incomplete bytes …); empty when nothing needs saying.</summary>
    public string Diagnostics { get; set; } = string.Empty;

    /// <summary>The adapter text of the trouble-code request, as received (frames), for diagnostics and exports.</summary>
    public string RawResponse { get; set; } = string.Empty;

    /// <summary>
    /// Where the displayed name comes from: "module" (the module reported it), "address-table" (our brand table
    /// lists this address under that name; not confirmed by the module) or "standard" (ISO 15765-4 OBD addresses).
    /// </summary>
    public string NameBasis { get; set; } = "address-table";

    public List<ModuleDtc> Codes { get; set; } = new();

    public string AddressText => Extended29Bit
        ? $"0x{TransmitCanId:X8}"
        : $"0x{TransmitCanId:X3}";
}

public sealed class ModuleDtc
{
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// The DTC bytes exactly as the module sent them (hex, status byte excluded). <see cref="Code"/> is that value written
    /// in the SAE P/C/B/U style, which is only meaningful when the module uses the SAE J2012 format; a manufacturer
    /// format (e.g. a BMW UDS code) keeps its true identity here.
    /// </summary>
    public string RawBytesHex { get; set; } = string.Empty;

    public byte FailureType { get; set; }
    public byte StatusByte { get; set; }
    public bool Active { get; set; }
    public bool Confirmed { get; set; }
    public bool Pending { get; set; }
    public string TitleKa { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;

    /// <summary>Freeze frame (conditions when the fault was stored), when the module provides it.</summary>
    public List<ModuleSnapshotValue> Snapshot { get; set; } = new();

    public string DisplayCode => FailureType == 0 ? Code : $"{Code}-{FailureType:X2}";
}

/// <summary>One value of a module freeze frame (e.g. speed 54 km/h).</summary>
public sealed class ModuleSnapshotValue
{
    public string NameKa { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
