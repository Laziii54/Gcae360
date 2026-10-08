using CarScanner.Models;
using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class DiagnosticReportPage : ContentPage
{
    public DiagnosticReportPage(DiagnosticReportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>
    /// "Open" on a saved scan: shows that scan's report and scrolls to it.
    /// (Handled here instead of a RelativeSource binding, which did not
    /// reach the page's view model from inside the BindableLayout list.)
    /// </summary>
    private async void OnOpenSessionClicked(object? sender, EventArgs e)
    {
        try
        {
        if (sender is not BindableObject { BindingContext: ScanSession session } ||
            BindingContext is not DiagnosticReportViewModel viewModel)
            return;

        viewModel.SelectSessionCommand.Execute(session);

        try { await Scroller.ScrollToAsync(DetailsCard, ScrollToPosition.Start, true); }
        catch { }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "DiagnosticReportPage.OnOpenSessionClicked");
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
        if (BindingContext is DiagnosticReportViewModel viewModel)
            await viewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "DiagnosticReportPage.OnAppearing");
        }
    }
}
