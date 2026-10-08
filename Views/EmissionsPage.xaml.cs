using CarScanner.Services.Localization;
using CarScanner.ViewModels;
namespace CarScanner.Views;
public partial class EmissionsPage:ContentPage
{ public EmissionsPage(EmissionsViewModel vm){InitializeComponent();BindingContext=vm;} protected override async void OnAppearing(){
    base.OnAppearing();
    try
    {
        ApplyUiLocalization(); if(BindingContext is EmissionsViewModel x){ x.Attach(); try{ await x.LoadCommand.ExecuteAsync(null); }catch{} }
    }
    catch (Exception commandException)
    {
        CarScanner.Services.CommandErrors.Report(commandException, "EmissionsPage.OnAppearing");
    } }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Release the singleton language/unit events (no page leak).
        (BindingContext as EmissionsViewModel)?.Detach();
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
