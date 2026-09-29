using CarScanner.Services.Localization;
using CarScanner.ViewModels;
namespace CarScanner.Views;
public partial class EmissionsPage:ContentPage
{ public EmissionsPage(EmissionsViewModel vm){InitializeComponent();BindingContext=vm;} protected override async void OnAppearing(){base.OnAppearing();
        ApplyUiLocalization(); if(BindingContext is EmissionsViewModel x){ try{ await x.LoadCommand.ExecuteAsync(null); }catch{} } } 
    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
