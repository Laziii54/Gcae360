using CarScanner.Services.Localization;
using CarScanner.ViewModels;
namespace CarScanner.Views;
public partial class EcuIdentifiersPage:ContentPage
{
    public EcuIdentifiersPage(EcuIdentifiersViewModel vm)
    {
        InitializeComponent();
        BindingContext=vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();
        if (BindingContext is EcuIdentifiersViewModel viewModel)
        {
            try
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
                await viewModel.StartPidUpdatesAsync();
            }
            catch { }
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is EcuIdentifiersViewModel viewModel)
            await viewModel.StopPidUpdatesAsync();
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
