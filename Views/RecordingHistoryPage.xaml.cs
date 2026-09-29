using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class RecordingHistoryPage : ContentPage
{
    public RecordingHistoryPage(RecordingHistoryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is RecordingHistoryViewModel viewModel)
            await viewModel.Load();
    }
}
