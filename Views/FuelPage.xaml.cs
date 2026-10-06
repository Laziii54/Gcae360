using CarScanner.Services.Localization;
using CarScanner.ViewModels; namespace CarScanner.Views; public partial class FuelPage:ContentPage{public FuelPage(FuelViewModel vm){InitializeComponent();BindingContext=vm;}
    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();
        if (BindingContext is FuelViewModel viewModel)
            viewModel.OnPageAppearing();
    }

    protected override async void OnDisappearing()
    {
        try
        {
        base.OnDisappearing();
        if (BindingContext is FuelViewModel viewModel)
            await viewModel.OnPageDisappearingAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "FuelPage.OnDisappearing");
        }
    }
}
