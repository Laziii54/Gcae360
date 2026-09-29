using CarScanner.VinCheck.ViewModels;

namespace CarScanner.VinCheck.Views;

public partial class VinPhotoGalleryPage : ContentPage, IQueryAttributable
{
    private IReadOnlyList<string> _images = [];
    private double _startScale = 1;

    public VinPhotoGalleryPage()
    {
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Selection", out var value) && value is PhotoSelection selection)
        {
            _images = selection.Images;
            Gallery.ItemsSource = _images;
            Gallery.Position = Math.Clamp(selection.Index, 0, Math.Max(0, _images.Count - 1));
            UpdateCounter();
        }
    }

    private void UpdateCounter() =>
        Counter.Text = _images.Count == 0 ? "0 / 0" : $"{Gallery.Position + 1} / {_images.Count}";

    private void PositionChanged(object? sender, PositionChangedEventArgs e) => UpdateCounter();

    private async void CloseClicked(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync(".."); } catch { }
    }

    private void PreviousClicked(object? sender, EventArgs e)
    {
        if (Gallery.Position > 0) Gallery.Position--;
    }

    private void NextClicked(object? sender, EventArgs e)
    {
        if (Gallery.Position < _images.Count - 1) Gallery.Position++;
    }

    private void PinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (sender is not Image image) return;
        if (e.Status == GestureStatus.Started) _startScale = image.Scale;
        else if (e.Status == GestureStatus.Running) image.Scale = Math.Clamp(_startScale * e.Scale, 1, 4);
        else if (e.Status == GestureStatus.Canceled) image.Scale = 1;
    }

    private void DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Image image) image.Scale = image.Scale > 1 ? 1 : 2.5;
    }
}
