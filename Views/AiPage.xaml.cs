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
        _viewModel.ChatMessages.CollectionChanged += OnChatMessagesChanged;
    }

    private void ApplyUiLocalization()
    {
        var localization = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalizationService>();
        localization?.Apply(this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyUiLocalization();
        ScrollChatToEnd();
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
