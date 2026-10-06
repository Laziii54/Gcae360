using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class HistoryPage : ContentPage
{
    public HistoryPage(HistoryViewModel vm){InitializeComponent();BindingContext=vm;}
    protected override async void OnAppearing(){
        try
        {base.OnAppearing();
        ApplyUiLocalization();if(BindingContext is HistoryViewModel vm)await vm.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HistoryPage.OnAppearing");
        }}

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
