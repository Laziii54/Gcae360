using CarScanner.ViewModels;
namespace CarScanner.Views;
public partial class BatteryPage:ContentPage { public BatteryPage(BatteryViewModel vm){InitializeComponent();BindingContext=vm;} }
