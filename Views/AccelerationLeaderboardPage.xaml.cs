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
        Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>()?.Apply(this);
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
