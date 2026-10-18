using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class LiveDataPage : ContentPage
{
    public LiveDataPage(LiveDataViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
        ApplyUiLocalization();
        if (BindingContext is LiveDataViewModel graphVm)
        {
            graphVm.GraphRequested -= OnGraphRequested;
            graphVm.GraphRequested += OnGraphRequested;
        }
        if (BindingContext is LiveDataViewModel vm) await vm.StartLiveDataCommand.ExecuteAsync(null);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "LiveDataPage.OnAppearing");
        }
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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        GraphSheet.Close();
        if (BindingContext is LiveDataViewModel graphVm)
            graphVm.GraphRequested -= OnGraphRequested;
        if (BindingContext is LiveDataViewModel vm) vm.StopLiveDataCommand.Execute(null);
    }

    private async void OnManufacturerDataTapped(object? sender, TappedEventArgs e)
    {
        try
        {
        var pro = Application.Current?.Handler?.MauiContext?.Services.GetService<CarScanner.Services.ProService>();
        if (pro is null)
            return;
        if (await CarScanner.Services.ProGate.EnsureAsync(pro, "მწარმოებლის მონაცემები", "Manufacturer data"))
            await CarScanner.Services.SafeNavigation.GoToAsync(nameof(ModuleLiveDataPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "LiveDataPage.OnManufacturerDataTapped");
        }
    }

    private async void OnGraphStudioTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await CarScanner.Services.SafeNavigation.GoToAsync(nameof(GraphStudioPage));
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "LiveDataPage.OnGraphStudioTapped");
        }
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
