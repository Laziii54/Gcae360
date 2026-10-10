using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class PassiveCanPage : ContentPage
{
    private readonly PassiveCanViewModel _viewModel;

    public PassiveCanPage(PassiveCanViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnDisappearing()
    {
        // Leaving the page stops listening.
        if (_viewModel.IsListening)
            _viewModel.Stop();
        base.OnDisappearing();
    }
}
