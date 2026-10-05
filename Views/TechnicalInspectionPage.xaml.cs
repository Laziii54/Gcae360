using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class TechnicalInspectionPage : ContentPage
{
    private readonly TechnicalInspectionViewModel _viewModel;

    public TechnicalInspectionPage(TechnicalInspectionViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.ScrollToTopRequested += OnScrollToTopRequested;
        _viewModel.RefreshModules();
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override void OnDisappearing()
    {
        _viewModel.ScrollToTopRequested -= OnScrollToTopRequested;
        base.OnDisappearing();
    }

    // Android back button inside a question returns to the category list instead of leaving the page.
    protected override bool OnBackButtonPressed()
    {
        if (_viewModel.IsQuestionMode)
        {
            _viewModel.CloseQuestionCommand.Execute(null);
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void OnScrollToTopRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await PageScroll.ScrollToAsync(0, 0, false);
            }
            catch
            {
                // Scrolling is cosmetic.
            }
        });
    }
}
