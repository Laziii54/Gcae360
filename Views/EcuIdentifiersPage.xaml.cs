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
        GraphSheet.Close();
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

    private void OnPidTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CarScanner.Models.EcuPidReading reading && reading.CanGraph)
            GraphSheet.Show(reading);
    }

    protected override bool OnBackButtonPressed()
    {
        if (GraphSheet.IsOpen)
        {
            GraphSheet.Close();
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
