using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class ModuleLiveDataPage : ContentPage
{
    public ModuleLiveDataPage(ModuleLiveDataViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
        (BindingContext as ModuleLiveDataViewModel)?.Attach();
        if (BindingContext is ModuleLiveDataViewModel vm && !vm.IsRunning)
            _ = vm.StartCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is ModuleLiveDataViewModel vm)
        {
            vm.StopCommand.Execute(null);
            vm.Detach();
        }
    }
}
