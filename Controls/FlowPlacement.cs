namespace CarScanner.Controls;

/// <summary>
/// Sequential cell placement for a tile grid: visible tiles take the next free cell, left to right and then row by row; a hidden
/// tile takes no cell. Pure logic (no MAUI types) so it can be unit-tested.
/// </summary>
public static class FlowPlacement
{
    /// <summary>Row and column of every tile; a hidden tile gets (0, 0) and is not counted.</summary>
    public static (int Row, int Column)[] Cells(IReadOnlyList<bool> visible, int columns)
    {
        var cols = Math.Max(1, columns);
        var cells = new (int Row, int Column)[visible.Count];
        var next = 0;
        for (var i = 0; i < visible.Count; i++)
        {
            if (!visible[i])
                continue;
            cells[i] = (next / cols, next % cols);
            next++;
        }

        return cells;
    }

    /// <summary>Number of rows needed for the visible tiles (at least one, so the grid keeps a valid definition).</summary>
    public static int Rows(IReadOnlyList<bool> visible, int columns) =>
        Math.Max(1, (visible.Count(v => v) + Math.Max(1, columns) - 1) / Math.Max(1, columns));
}
