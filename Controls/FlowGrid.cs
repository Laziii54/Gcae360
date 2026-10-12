namespace CarScanner.Controls;

/// <summary>
/// A grid of equal-width columns whose children flow into the next free cell in order. Children do not carry Grid.Row /
/// Grid.Column; a child that is hidden (IsVisible = false) is skipped, so it never leaves an empty cell. Sizes, spacing and
/// styles are those of the Grid it replaces.
/// </summary>
public sealed class FlowGrid : Grid
{
    public static readonly BindableProperty ColumnsProperty = BindableProperty.Create(
        nameof(Columns), typeof(int), typeof(FlowGrid), 3,
        propertyChanged: (b, _, _) => ((FlowGrid)b).Reflow());

    private bool _reflowing;

    public FlowGrid()
    {
        ChildAdded += (_, e) =>
        {
            if (e.Element is VisualElement child)
                child.PropertyChanged += OnChildPropertyChanged;
            Reflow();
        };
        ChildRemoved += (_, e) =>
        {
            if (e.Element is VisualElement child)
                child.PropertyChanged -= OnChildPropertyChanged;
            Reflow();
        };
    }

    /// <summary>Number of equal columns (default 3).</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    private void OnChildPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == VisualElement.IsVisibleProperty.PropertyName)
            Reflow();
    }

    private void Reflow()
    {
        if (_reflowing)
            return;

        _reflowing = true;
        try
        {
            var columns = Math.Max(1, Columns);
            var tiles = Children.OfType<VisualElement>().ToList();
            var visible = tiles.Select(tile => tile.IsVisible).ToList();

            if (ColumnDefinitions.Count != columns)
            {
                var definitions = new ColumnDefinitionCollection();
                for (var i = 0; i < columns; i++)
                    definitions.Add(new ColumnDefinition(GridLength.Star));
                ColumnDefinitions = definitions;
            }

            var rows = FlowPlacement.Rows(visible, columns);
            if (RowDefinitions.Count != rows)
            {
                var definitions = new RowDefinitionCollection();
                for (var i = 0; i < rows; i++)
                    definitions.Add(new RowDefinition(GridLength.Auto));
                RowDefinitions = definitions;
            }

            var cells = FlowPlacement.Cells(visible, columns);
            for (var i = 0; i < tiles.Count; i++)
            {
                if (!visible[i])
                    continue;
                SetRow((BindableObject)tiles[i], cells[i].Row);
                SetColumn((BindableObject)tiles[i], cells[i].Column);
            }
        }
        finally
        {
            _reflowing = false;
        }
    }

}
