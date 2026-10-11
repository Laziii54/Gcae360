namespace CarScanner.Models;

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
