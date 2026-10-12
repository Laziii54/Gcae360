namespace CarScanner.ViewModels;

/// <summary>One answered value of the Service search, as shown in the search card.</summary>
/// <summary><c>Mark</c> is an icon kind from Controls/IconCatalog (not a glyph character).</summary>
public sealed record ServiceSearchHitRow(string Mark, string MarkColor, string Title, string Detail, string SourceKey = "", bool CanConfirm = false);
