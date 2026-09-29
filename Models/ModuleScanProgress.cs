namespace CarScanner.Models;

/// <summary>Represents a completed, real step of the generic OBD module scan.</summary>
public sealed record ModuleScanProgress(int Percent, string Stage);
