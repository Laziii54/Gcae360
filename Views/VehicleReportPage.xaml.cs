using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class VehicleReportPage : ContentPage
{
    public VehicleReportPage(VehicleReportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>()?.Apply(this);
            (BindingContext as VehicleReportViewModel)?.Refresh();
        }
        catch (Exception exception)
        {
            CarScanner.Services.CommandErrors.Report(exception, "VehicleReportPage.OnAppearing");
        }
    }
}
