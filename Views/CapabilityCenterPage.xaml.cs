using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class CapabilityCenterPage : ContentPage
{
    private readonly CapabilityCenterViewModel _viewModel;

    public CapabilityCenterPage(CapabilityCenterViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
        _viewModel.ApplyCurrentIfAny();
        if (!_viewModel.IsBusy)
            await _viewModel.RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "CapabilityCenterPage.OnAppearing");
        }
    }
}
