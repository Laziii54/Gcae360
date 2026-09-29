namespace CarScanner.Models;

/// <summary>A displayed result from an adapter command that was actually attempted.</summary>
public sealed class AdapterTestResult
{
    public string Name { get; init; } = "";
    public string Status { get; init; } = "Not Supported";
    public string Details { get; init; } = "";

    public string Color => Status switch
    {
        "Passed" => "#4ADE80",
        "Failed" => "#F87171",
        _ => "#F59E0B"
    };
}
