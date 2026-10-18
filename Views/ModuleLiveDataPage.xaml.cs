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

    private void OnGraphRequested(object item) => GraphSheet.Show(item);

    protected override bool OnBackButtonPressed()
    {
        if (GraphSheet.IsOpen)
        {
            GraphSheet.Close();
            return true;
        }
        return base.OnBackButtonPressed();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
        (BindingContext as ModuleLiveDataViewModel)?.Attach();
        if (BindingContext is ModuleLiveDataViewModel graphVm)
        {
            graphVm.GraphRequested -= OnGraphRequested;
            graphVm.GraphRequested += OnGraphRequested;
        }
        if (BindingContext is ModuleLiveDataViewModel vm && !vm.IsRunning)
            _ = vm.StartCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        GraphSheet.Close();
        if (BindingContext is ModuleLiveDataViewModel vm)
        {
            vm.GraphRequested -= OnGraphRequested;
            vm.StopCommand.Execute(null);
            vm.Detach();
        }
    }
}
