using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class DiagnosticsPage : ContentPage, IQueryAttributable
{
    private readonly DiagnosticsViewModel _viewModel;

    public DiagnosticsPage(DiagnosticsViewModel vm)
    {
        InitializeComponent();
        _viewModel = vm;
        BindingContext = vm;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("mode", out var mode) && mode?.ToString() == "freeze")
            MainThread.BeginInvokeOnMainThread(() => _viewModel.ReadFreezeFrameCommand.Execute(null));
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();
    }
}
