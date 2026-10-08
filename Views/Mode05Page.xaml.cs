using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class Mode05Page : ContentPage
{
    private readonly Mode05ViewModel _viewModel;

    public Mode05Page(Mode05ViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
        if (!_viewModel.IsBusy)
            await _viewModel.RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "Mode05Page.OnAppearing");
        }
    }
}
