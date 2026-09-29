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

    public List<ModuleDtc> Codes { get; set; } = new();

    public string AddressText => Extended29Bit
        ? $"0x{TransmitCanId:X8}"
        : $"0x{TransmitCanId:X3}";
}

public sealed class ModuleDtc
{
    public string Code { get; set; } = string.Empty;
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

/// <summary>One line in the "Modules" card of the scan page.</summary>
public sealed class ModuleStatusRow
{
    public string Name { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public Color StatusColor { get; init; } = Colors.Gray;

    /// <summary>Module's own name / software version (small grey line), may be empty.</summary>
    public string Detail { get; init; } = string.Empty;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}

/// <summary>One module card on the ECU page: name, address and identification lines.</summary>
public sealed class ModuleInfoCard
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Lines { get; init; } = string.Empty;
    public bool HasLines => !string.IsNullOrWhiteSpace(Lines);
}
