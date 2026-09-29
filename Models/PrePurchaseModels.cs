using CommunityToolkit.Mvvm.ComponentModel;

namespace CarScanner.Models;

/// <summary>One step of the pre-purchase check (✓ done, ◌ running, ○ waiting).</summary>
public sealed partial class PrePurchaseStep : ObservableObject
{
    public string Key { get; init; } = string.Empty;
    public string TextKa { get; init; } = string.Empty;
    public string TextEn { get; init; } = string.Empty;

    [ObservableProperty] private string text = string.Empty;
    [ObservableProperty] private string color = "#94A3B8";

    /// <summary>0 waiting, 1 running, 2 done, 3 skipped.</summary>
    public int State { get; private set; }

    public void Set(int state, bool english)
    {
        State = state;
        var label = english ? TextEn : TextKa;
        (Text, Color) = state switch
        {
            2 => ($"✓ {label}", "#4ADE80"),
            1 => ($"◌ {label}…", "#60A5FA"),
            3 => ($"– {label}", "#94A3B8"),
            _ => ($"○ {label}", "#94A3B8")
        };
    }
}

/// <summary>One line of a result card: label on the left, value on the right.</summary>
public sealed class PrePurchaseRow
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Color { get; init; } = "#4ADE80";
}

/// <summary>Complete result of a pre-purchase check.</summary>
public sealed class PrePurchaseResult
{
    public DateTime FinishedAt { get; init; } = DateTime.Now;
    public ObdScanReport Report { get; init; } = new();
    public int Score { get; set; }
    public string VerdictKa { get; set; } = string.Empty;
    public string VerdictEn { get; set; } = string.Empty;
    public string SummaryKa { get; set; } = string.Empty;
    public string SummaryEn { get; set; } = string.Empty;
    public string Color { get; set; } = "#4ADE80";

    public List<PrePurchaseFinding> Faults { get; } = new();
    public List<PrePurchaseFinding> Mileage { get; } = new();
    public List<PrePurchaseFinding> Condition { get; } = new();
    public List<PrePurchaseFinding> History { get; } = new();

    public string MileageBadgeKa { get; set; } = string.Empty;
    public string MileageBadgeEn { get; set; } = string.Empty;
    public string MileageBadgeColor { get; set; } = "#94A3B8";

    /// <summary>Plain-text summary of every finding, given to the AI.</summary>
    public string AiContext { get; set; } = string.Empty;
}

/// <summary>A finding in both languages (label/value) with a colour.</summary>
public sealed class PrePurchaseFinding
{
    public string LabelKa { get; init; } = string.Empty;
    public string LabelEn { get; init; } = string.Empty;
    public string ValueKa { get; init; } = string.Empty;
    public string ValueEn { get; init; } = string.Empty;
    public string Color { get; init; } = "#4ADE80";

    public PrePurchaseRow ToRow(bool english) => new()
    {
        Label = english ? LabelEn : LabelKa,
        Value = english ? ValueEn : ValueKa,
        Color = Color
    };
}

/// <summary>One chat bubble on the AI consultation page.</summary>
public sealed class PrePurchaseChatMessage
{
    public string Text { get; init; } = string.Empty;
    public bool IsUser { get; init; }
    public bool IsAi => !IsUser;
}
