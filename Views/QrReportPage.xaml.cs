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
        try
        {
        _viewModel.Attach();
        Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>()?.Apply(this);
        if (!_viewModel.HasQr)
            await _viewModel.CreateCommand.ExecuteAsync(null);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "QrReportPage.OnAppearing");
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.Cancel();
        _viewModel.Detach();
        base.OnDisappearing();
    }
}
