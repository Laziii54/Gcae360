using CarScanner.Services.Localization;
using CarScanner.ViewModels; namespace CarScanner.Views; public partial class ModuleScanPage:ContentPage{private readonly ModuleScanViewModel _vm; public ModuleScanPage(ModuleScanViewModel vm){InitializeComponent();BindingContext=vm;_vm=vm;}
    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Attach();
        ApplyUiLocalization();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Detach();
    }
}