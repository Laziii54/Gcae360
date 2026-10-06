using CarScanner.ViewModels;

namespace CarScanner.Views;

public partial class ServicePage : ContentPage
{
    private readonly ServiceViewModel _viewModel;

    public ServicePage(ServiceViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
#if DEBUG
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Service log",
            Command = new Command(async () =>
            {
                try
                {
                    var path = Path.Combine(FileSystem.CacheDirectory, "service-diagnostic-trace.json");
                    await File.WriteAllTextAsync(path,
                        CarScanner.Services.OemDiagnostics.ServiceDiagnosticTrace.ExportJson());
                    await Share.Default.RequestAsync(new ShareFileRequest { Title = "Service log", File = new ShareFile(path) });
                }
                catch (Exception)
                {
                    await DisplayAlertAsync("Service log", "Log export failed / ლოგის ექსპორტი ვერ შესრულდა", "OK");
                }
            })
        });
#endif
    }

    protected override async void OnAppearing()
    {
        try
        {
        base.OnAppearing();
        _viewModel.ReloadMaintenanceParts();

        // Let the page open smoothly first; the ECU reads start right after.
        await Task.Delay(250);

        try
        {
            if (!_viewModel.IsBusy)
                await _viewModel.RefreshCommand.ExecuteAsync(null);
        }
        catch
        {
            // Refresh reports its own errors on the page.
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ServicePage.OnAppearing");
        }
    }

    /// <summary>A search value tapped: confirm it is what the dashboard shows.</summary>
    private async void OnSearchHitTapped(object? sender, TappedEventArgs e)
    {
        try
        {
        if ((sender as BindableObject)?.BindingContext is not ServiceSearchHitRow row || !row.CanConfirm)
            return;

        var english = Application.Current?.Handler?.MauiContext?.Services
            .GetService<CarScanner.Services.Localization.LocalizationService>()?.IsEnglish == true;
        var yes = await DisplayAlertAsync(
            row.Title,
            english ? "Does the car's dashboard show the same value?" : "მანქანის პანელზეც იგივე მნიშვნელობა ჩანს?",
            english ? "Yes, correct" : "კი, სწორია",
            english ? "No" : "არა");
        if (yes)
            await _viewModel.ConfirmSearchHitAsync(row);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "ServicePage.OnSearchHitTapped");
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.CancelRefresh();

        if (_viewModel.IsActiveTestRunning)
            _viewModel.StopActiveTestCommand.Execute(null);

        base.OnDisappearing();
    }
}
