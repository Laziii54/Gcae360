using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class ResetRecorderPage : ContentPage
{
    private readonly ResetRecorderViewModel _viewModel;

    public ResetRecorderPage(ResetRecorderViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnDisappearing()
    {
        // Leaving the page stops listening (nothing is ever sent to the car).
        if (_viewModel.IsRecording)
            _viewModel.CancelCommand.Execute(null);
        base.OnDisappearing();
    }
}
