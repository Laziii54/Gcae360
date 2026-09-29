using CarScanner.Services.Localization;
using CarScanner.ViewModels; namespace CarScanner.Views; public partial class ModuleScanPage:ContentPage{public ModuleScanPage(ModuleScanViewModel vm){InitializeComponent();BindingContext=vm;}
    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();
    }
}