using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class ResearchDataPage : ContentPage
{
    public ResearchDataPage(ResearchDataViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
        // Manual only: the admin presses "კვლევა" themselves, unlike Module Live Data's auto-start.
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is ResearchDataViewModel vm)
            vm.StopCommand.Execute(null);
    }
}
