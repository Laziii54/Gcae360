using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class CustomsCalculatorPage : ContentPage
{
    private readonly CustomsCalculatorViewModel _viewModel;

    public CustomsCalculatorPage(CustomsCalculatorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _viewModel.LoadAsync(); }
        catch { }
    }
}
