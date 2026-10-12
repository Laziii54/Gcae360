namespace CarScanner.Controls;

/// <summary>
/// The one icon vocabulary of the OBD360 user interface: monochrome outline strokes on a 24 x 24 grid
/// (round caps and joins, 1.7 stroke at 20 px - the same recipe as RowIconStyle). Icons are not emoji, not
/// Unicode symbols and never coloured pictograms. Add a new entry here only when text alone cannot carry the
/// meaning; see DEVELOPMENT_GUIDE.md (Icon policy). Names are lower-case kebab-case.
/// </summary>
public static class IconCatalog
{
    public const string Close = "close";
    public const string Check = "check";
    public const string Minus = "minus";
    public const string Circle = "circle";
    public const string Approx = "approx";
    public const string ChevronRight = "chevron-right";
    public const string ChevronLeft = "chevron-left";
    public const string ChevronDown = "chevron-down";
    public const string ChevronUp = "chevron-up";
    public const string Refresh = "refresh";
    public const string Play = "play";
    public const string Pause = "pause";
    public const string Stop = "stop";
    public const string Record = "record";
    public const string Flag = "flag";
    public const string Expand = "expand";
    public const string Menu = "menu";
    public const string Star = "star";
    public const string Send = "send";
    public const string Info = "info";
    public const string Warning = "warning";

    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        [Close] = "M6,6 L18,18 M18,6 L6,18",
        [Check] = "M5,12.5 L10,17 L19,7",
        [Minus] = "M6,12 L18,12",
        [Circle] = "M12,6 A6,6 0 1 1 11.99,6 Z",
        [Approx] = "M5,9.5 C7.5,6.5 10,12.5 12,9.5 C14,6.5 16.5,12.5 19,9.5 M5,16 C7.5,13 10,19 12,16 C14,13 16.5,19 19,16",
        [ChevronRight] = "M9,6 L15,12 L9,18",
        [ChevronLeft] = "M15,6 L9,12 L15,18",
        [ChevronDown] = "M6,9 L12,15 L18,9",
        [ChevronUp] = "M6,15 L12,9 L18,15",
        [Refresh] = "M20,12 A8,8 0 1 1 17.6,6.3 M20,4 L20,9 L15,9",
        [Play] = "M8,5 L19,12 L8,19 Z",
        [Pause] = "M8,5 L8,19 M16,5 L16,19",
        [Stop] = "M7,7 L17,7 L17,17 L7,17 Z",
        [Record] = "M12,5 A7,7 0 1 1 11.99,5 Z",
        [Flag] = "M6,21 L6,4 M6,5 L18,5 L15,9 L18,13 L6,13",
        [Expand] = "M4,9 L4,4 L9,4 M15,4 L20,4 L20,9 M20,15 L20,20 L15,20 M9,20 L4,20 L4,15",
        [Menu] = "M5,7 L19,7 M5,12 L19,12 M5,17 L19,17",
        [Star] = "M12,4 L14.4,9.3 L20,10 L15.8,13.9 L17,19.5 L12,16.6 L7,19.5 L8.2,13.9 L4,10 L9.6,9.3 Z",
        [Send] = "M20,4 L10,14 M20,4 L14,20 L10,14 L4,10 Z",
        [Info] = "M12,3 A9,9 0 1 1 11.99,3 Z M12,11 L12,17 M12,7.5 L12,7.6",
        [Warning] = "M12,4 L21,19 L3,19 Z M12,10 L12,14 M12,16.5 L12,16.6",
    };

    public static IReadOnlyCollection<string> Names => Paths.Keys;

    /// <summary>The raw path data of an icon, or null when the name is unknown (the icon then draws nothing).</summary>
    public static string? PathData(string? name) =>
        !string.IsNullOrWhiteSpace(name) && Paths.TryGetValue(name!, out var data) ? data : null;
}
