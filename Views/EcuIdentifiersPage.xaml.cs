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
        try
        {
        ApplyUiLocalization();
        if (BindingContext is EcuIdentifiersViewModel viewModel)
        {
            viewModel.Attach();
            try
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
                await viewModel.StartPidUpdatesAsync();
            }
            catch { }
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "EcuIdentifiersPage.OnAppearing");
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        try
        {
        if (BindingContext is EcuIdentifiersViewModel viewModel)
        {
            viewModel.Detach();
            await viewModel.StopPidUpdatesAsync();
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "EcuIdentifiersPage.OnDisappearing");
        }
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
