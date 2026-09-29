using CarScanner.VinCheck.ViewModels;

namespace CarScanner.VinCheck.Views;

public partial class VinLookupPage : ContentPage
{
    private readonly VinLookupViewModel _vm;

    public VinLookupPage(VinLookupViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.RefreshTexts();
        _vm.PrefillFromConnectedCar();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Leaving VIN Check (not just opening a report): stop a running search.
        if (Shell.Current?.Navigation.NavigationStack.Contains(this) != true)
            _vm.Cancel();
    }

    private void OnEntryFocused(object? sender, FocusEventArgs e) => _vm.IsEditing = true;

    private void OnEntryUnfocused(object? sender, FocusEventArgs e) => _vm.IsEditing = false;
}
