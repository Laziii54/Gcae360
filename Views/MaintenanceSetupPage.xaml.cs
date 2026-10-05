using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class MaintenanceSetupPage : ContentPage
{
    private readonly MaintenanceSetupViewModel _viewModel;

    public MaintenanceSetupPage(MaintenanceSetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Load();
    }
}
