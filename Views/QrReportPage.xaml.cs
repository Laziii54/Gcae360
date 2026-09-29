using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class QrReportPage : ContentPage
{
    private readonly QrReportViewModel _viewModel;
    public QrReportPage(QrReportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>()?.Apply(this);
        if (!_viewModel.HasQr)
            await _viewModel.CreateCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        _viewModel.Cancel();
        base.OnDisappearing();
    }
}
