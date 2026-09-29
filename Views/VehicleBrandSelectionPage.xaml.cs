using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class VehicleBrandSelectionPage : ContentPage
{
    public VehicleBrandSelectionPage(VehicleBrandSelectionViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
