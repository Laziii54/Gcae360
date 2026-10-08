using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class AccelerationLeaderboardPage : ContentPage
{
    private readonly AccelerationLeaderboardViewModel _viewModel;

    public AccelerationLeaderboardPage(AccelerationLeaderboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
        Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>()?.Apply(this);
        await _viewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AccelerationLeaderboardPage.OnAppearing");
        }
    }

    // Android back button closes the filter sheet first.
    protected override bool OnBackButtonPressed()
    {
        if (_viewModel.IsFilterOpen)
        {
            _viewModel.IsFilterOpen = false;
            return true;
        }
        return base.OnBackButtonPressed();
    }
}
