using CarScanner.Services.Localization;
using CarScanner.ViewModels;
using System.Collections.Specialized;

namespace CarScanner.Views;

public partial class AiPage : ContentPage
{
    private readonly AiDiagnosticViewModel _viewModel;

    public AiPage(AiDiagnosticViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _viewModel = vm;
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            // The view model is a singleton: subscribe only while this page is
            // visible so a closed page is not kept alive by the collection.
            _viewModel.ChatMessages.CollectionChanged -= OnChatMessagesChanged;
            _viewModel.ChatMessages.CollectionChanged += OnChatMessagesChanged;
            ApplyUiLocalization();
            ScrollChatToEnd();
            await _viewModel.EnsureCurrentVehicleAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "AiPage.OnAppearing");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ChatMessages.CollectionChanged -= OnChatMessagesChanged;
    }

    private void OnChatMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(ScrollChatToEnd);

    private void ScrollChatToEnd()
    {
        if (_viewModel.ChatMessages.Count == 0)
            return;

        ChatMessagesList.ScrollTo(
            _viewModel.ChatMessages[^1],
            position: ScrollToPosition.End,
            animate: true);
    }
}
