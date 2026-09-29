namespace CarScanner.Models;

/// <summary>One decoded value from a real Mode 02 Freeze Frame response.</summary>
public sealed class FreezeFrameValue
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string SourceText => "რეალური ECU snapshot";
}
