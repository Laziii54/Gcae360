using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class Mode06Page : ContentPage
{
    private readonly Mode06ViewModel _viewModel;

    public Mode06Page(Mode06ViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_viewModel.IsBusy)
            await _viewModel.RefreshCommand.ExecuteAsync(null);
    }
}
