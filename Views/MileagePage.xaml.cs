using CarScanner.Services.Localization;
using CarScanner.ViewModels; namespace CarScanner.Views; public partial class MileagePage:ContentPage{public MileagePage(MileageViewModel vm){InitializeComponent();BindingContext=vm;}
    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override async void OnAppearing()
    {
        try
        {
        base.OnAppearing();
        ApplyUiLocalization();
        if (BindingContext is MileageViewModel viewModel)
            await viewModel.LoadVehicleContextAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "MileagePage.OnAppearing");
        }
    }
}
