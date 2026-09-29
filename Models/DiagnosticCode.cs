using System;

namespace CarScanner.Models;

public enum DiagnosticCodeCategory { Powertrain, Body, Chassis, Network }
public enum DiagnosticCodeStatus { Stored, Pending, Permanent }

public sealed class DiagnosticCode
{
    public string Code { get; }
    public string Description { get; }
    public DiagnosticCodeCategory Category { get; }
    public DiagnosticCodeStatus Status { get; }

    public DiagnosticCode(string code, string description, DiagnosticCodeCategory category, DiagnosticCodeStatus status)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Category = category;
        Status = status;
    }

    public override string ToString() => $"{Code} - {Description}";
}
