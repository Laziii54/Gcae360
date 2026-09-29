using CommunityToolkit.Mvvm.ComponentModel;

namespace CarScanner.Models;

/// <summary>
/// Status of an ECU that actually answered during a scan.  It deliberately
/// distinguishes an unavailable OEM module from a discovered module with no
/// faults, so the topology never implies communication that did not occur.
/// </summary>
public sealed partial class ModuleScanItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string DefinitionId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public bool IsGenericObd { get; init; }
    public bool IsDiscovered { get; init; }
    public string CommunicationText { get; init; } = "No communication";
    public string CommunicationColor { get; init; } = "#94A3B8";
    public string AvailabilityText { get; init; } = "AVAILABLE";
    public string AvailabilityColor { get; init; } = "#4ADE80";
    public int StoredFaultCount { get; init; }
    public int PendingFaultCount { get; init; }
    public int PermanentFaultCount { get; init; }
    public int DtcServicesRead { get; init; } = 3;
    public int DtcServicesExpected { get; init; } = 3;
    public bool PendingDtcRead { get; init; } = true;
    public string DataSummary { get; init; } = "ECU მონაცემი მიუწვდომელია";
    public string CapabilitiesSummary { get; init; } = "Capabilities: Not Checked";
    public string IdentificationCapability { get; init; } = "IDENTIFICATION.READ: Not Checked";
    public string DtcCapability { get; init; } = "DTC.READ: Not Checked";
    public string LiveDataCapability { get; init; } = "DATA.READ: Not Checked";
    public string FreezeFrameCapability { get; init; } = "FREEZEFRAME.READ: Not Checked";
    public string OemDtcSummary { get; init; } = "DTC: Not Checked";
    public string OemFreezeFrameSummary { get; init; } = "Freeze Frame: Not Checked";
    public string SourceText { get; init; } = "Generic OBD-II";
    public DateTime? LastScanAt { get; init; }

    public int TotalFaultCount => StoredFaultCount + PendingFaultCount + PermanentFaultCount;
    public string FaultText => !IsGenericObd
        ? OemDtcSummary
        : DtcServicesRead == 0
        ? "DTC სტატუსი მიუწვდომელია"
        : TotalFaultCount == 0
            ? DtcServicesRead == DtcServicesExpected
                ? "აქტიური/შენახული კოდი არ არის"
                : $"DTC წაკითხვა ნაწილობრივ დასრულდა ({DtcServicesRead}/{DtcServicesExpected})"
            : $"{TotalFaultCount} კოდი" + (DtcServicesRead == DtcServicesExpected ? string.Empty : $" · ნაწილობრივი წაკითხვა {DtcServicesRead}/{DtcServicesExpected}");
    public string PendingText => !IsGenericObd
        ? string.Empty
        : !PendingDtcRead
        ? "Pending DTC სტატუსი მიუწვდომელია"
        : PendingFaultCount == 0
            ? "Pending კოდი არ არის"
            : $"Pending: {PendingFaultCount}";
    public string FaultColor => !IsGenericObd
        ? OemDtcSummary.Contains("კოდი არ აღმოჩნდა", StringComparison.Ordinal) ? "#4ADE80"
            : System.Text.RegularExpressions.Regex.IsMatch(OemDtcSummary, @"OEM DTC: \d") ? "#F87171" : "#94A3B8"
        : DtcServicesRead == 0 ? "#94A3B8" : TotalFaultCount == 0 && DtcServicesRead == DtcServicesExpected ? "#4ADE80" : "#F59E0B";
    public string LastScanText => LastScanAt is null ? "ჯერ არ დასკანირებულა" : $"განახლდა: {LastScanAt:HH:mm:ss}";
}

public sealed partial class ModuleFaultItem : ObservableObject
{
    public string DefinitionId { get; init; } = string.Empty;
    public string EcuName { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Severity { get; init; } = "შესამოწმებელი";

    /// <summary>Report dot: red for a current fault, amber for history/stored-only.</summary>
    public Color DotColor =>
        Severity.Contains("ისტორ", StringComparison.OrdinalIgnoreCase) ||
        Severity.Contains("history", StringComparison.OrdinalIgnoreCase)
            ? Color.FromArgb("#C98A1A")
            : Color.FromArgb("#B3361F");

    /// <summary>Small grey line under the description: module · status.</summary>
    public string MetaText => string.Join(" · ", new[] { EcuName, Status }.Where(part => !string.IsNullOrWhiteSpace(part)));
    public string Details => string.IsNullOrWhiteSpace(EcuName)
        ? "სტატუსი ECU-ის პასუხიდანაა. შესაძლო მიზეზები და ნაბიჯები იხილე კოდების დეტალურ გვერდზე."
        : $"ECU: {EcuName} · სტატუსი ECU-ის პასუხიდანაა.";

    // Possible causes and first checks (from the offline catalog).
    [ObservableProperty] private string causesText = string.Empty;
    [ObservableProperty] private string checksText = string.Empty;
    [ObservableProperty] private string causesHeader = "შესაძლო მიზეზები";
    [ObservableProperty] private string checksHeader = "რა შევამოწმოთ";

    // AI explanation of this code.
    [ObservableProperty] private string aiExplanation = string.Empty;
    [ObservableProperty] private bool isAiBusy;
    [ObservableProperty] private string aiButtonText = "AI ახსნა";

    public bool HasCauses => !string.IsNullOrWhiteSpace(CausesText);
    public bool HasChecks => !string.IsNullOrWhiteSpace(ChecksText);
    public bool HasAiExplanation => !string.IsNullOrWhiteSpace(AiExplanation);
    public bool IsAiIdle => !IsAiBusy;

    partial void OnCausesTextChanged(string value) => OnPropertyChanged(nameof(HasCauses));
    partial void OnChecksTextChanged(string value) => OnPropertyChanged(nameof(HasChecks));
    partial void OnAiExplanationChanged(string value) => OnPropertyChanged(nameof(HasAiExplanation));
    partial void OnIsAiBusyChanged(bool value) => OnPropertyChanged(nameof(IsAiIdle));
}
