using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class SensorsPage : ContentPage
{
    public SensorsPage(SensorsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        // The graph polls only while the sheet is open.
        GraphSheet.Closed += (_, _) => vm.StopGraph();
    }

    private void OnSensorTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CarScanner.Models.SensorValue sensor && sensor.CanGraph && BindingContext is SensorsViewModel vm)
        {
            var source = vm.StartGraph(sensor);
            if (source is not null)
                GraphSheet.Show(source);
        }
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
    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();
        (BindingContext as SensorsViewModel)?.Attach();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        GraphSheet.Close();
        (BindingContext as SensorsViewModel)?.StopGraph();
        (BindingContext as SensorsViewModel)?.Detach();
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
