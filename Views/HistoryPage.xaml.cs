using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class HistoryPage : ContentPage
{
    public HistoryPage(HistoryViewModel vm){InitializeComponent();BindingContext=vm;}
    protected override async void OnAppearing(){base.OnAppearing();
        ApplyUiLocalization();if(BindingContext is HistoryViewModel vm)await vm.LoadCommand.ExecuteAsync(null);}

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }
}
