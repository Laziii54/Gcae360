using CarScanner.ViewModels;
namespace CarScanner.Views;
public partial class VehicleHealthPage : ContentPage
{
    public VehicleHealthPage(VehicleHealthViewModel viewModel) { InitializeComponent(); BindingContext = viewModel; }
}
