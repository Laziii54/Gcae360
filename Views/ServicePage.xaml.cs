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
        base.OnAppearing();

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

    protected override void OnDisappearing()
    {
        _viewModel.CancelRefresh();

        if (_viewModel.IsActiveTestRunning)
            _viewModel.StopActiveTestCommand.Execute(null);

        base.OnDisappearing();
    }
}
