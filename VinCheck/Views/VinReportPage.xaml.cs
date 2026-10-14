using CarScanner.VinCheck.ViewModels;

namespace CarScanner.VinCheck.Views;

public partial class VinReportPage : ContentPage
{
    private readonly VinReportViewModel _vm;

    public VinReportPage(VinReportViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Back from the bank page: show the paid report if it is ready.
        _ = _vm.LoadFullAsync();
    }

    private async void BackClicked(object? sender, EventArgs e)
    {
        try { await CarScanner.Services.SafeNavigation.GoToAsync(".."); } catch { }
    }
}
