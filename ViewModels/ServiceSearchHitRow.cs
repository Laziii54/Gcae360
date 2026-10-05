namespace CarScanner.ViewModels;

/// <summary>One answered value of the Service search, as shown in the search card.</summary>
public sealed record ServiceSearchHitRow(string Mark, string MarkColor, string Title, string Detail, string SourceKey = "", bool CanConfirm = false);
