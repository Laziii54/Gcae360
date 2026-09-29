namespace CarScanner.Models;

public sealed record PidDefinition(
    int Pid,
    string Name,
    string Unit,
    string Category,
    double Min,
    double Max,
    string Formula,
    string Description,
    bool ProOnly = false);
