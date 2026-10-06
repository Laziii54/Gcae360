using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class AccelerationHistoryPage : ContentPage
{
    private readonly AccelerationHistoryViewModel _viewModel;

    public AccelerationHistoryPage(AccelerationHistoryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        try
        {
        base.OnAppearing();
        await _viewModel.RefreshAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccelerationHistoryPage.OnAppearing");
        }
    }
}
