namespace CarScanner.Models;
public sealed class EcuIdentifier
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public bool Available => !string.IsNullOrWhiteSpace(Value) && Value != "მიუწვდომელია";
}
