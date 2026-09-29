using CarScanner.VinCheck.ViewModels;

namespace CarScanner.VinCheck.Views;

public partial class VinHistoryPage : ContentPage
{
    private readonly VinHistoryViewModel _vm;

    public VinHistoryPage(VinHistoryViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _vm.LoadAsync(); } catch { }
    }

    private async void BackClicked(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync(".."); } catch { }
    }
}
