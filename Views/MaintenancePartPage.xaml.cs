using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class MaintenancePartPage : ContentPage
{
    public MaintenancePartPage(MaintenancePartViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
