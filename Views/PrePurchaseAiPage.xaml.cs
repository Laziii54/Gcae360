using CarScanner.Services.Localization;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class PrePurchaseAiPage : ContentPage
{
    private readonly PrePurchaseAiViewModel _viewModel;

    public PrePurchaseAiPage(PrePurchaseAiViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _viewModel = vm;
    }

    private async void OnMessagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        try
        {
            await Task.Delay(80);
            await ChatScroll.ScrollToAsync(0, ChatScroll.ContentSize.Height, true);
        }
        catch
        {
            // Scrolling is cosmetic; the page may be closing.
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
        // Singleton view model: subscribe only while visible (no page leak).
        _viewModel.Messages.CollectionChanged -= OnMessagesChanged;
        _viewModel.Messages.CollectionChanged += OnMessagesChanged;
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
        await _viewModel.StartAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "PrePurchaseAiPage.OnAppearing");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Messages.CollectionChanged -= OnMessagesChanged;
    }
}
