using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CarScanner.Services;
using CarScanner.VinCheck.Services;
using CarScanner.VinCheck.Views;

namespace CarScanner.VinCheck.ViewModels;

public partial class VinLookupViewModel(
    IVinValidatorService validator,
    IVinHistoryService vinService,
    IVinHistoryDatabaseService database,
    VehicleInfoService vehicleInfo) : ObservableObject
{
    private CancellationTokenSource? _searchCts;

    [ObservableProperty] private string vin = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string errorMessage = "";

    public string Title => VinText.T("VinHistory");
    public string Subtitle => VinText.T("Subtitle");
    public string Placeholder => VinText.T("EnterVin");
    public string ButtonText => VinText.T("CheckVin");
    public string LoadingText => VinText.T("Searching");
    public string SampleHint => VinText.T("SampleHint");
    public string HistoryText => VinText.T("History");
    public string FavoritesText => VinText.T("Favorites");
    public string LanguageText => VinText.T("Language");
    public string VinCodeText => VinText.T("VinCode");
    public string TapHintText => VinText.T("TapHint");
    public string ClearText => VinText.T("Clear");
    public IReadOnlyList<string> Features =>
        new[] { "FeatPhotos", "FeatDamage", "FeatMileage", "FeatTitle", "FeatRecalls" }.Select(VinText.T).ToList();

    // ============================================================
    // 17 BOXES (one character each)
    // ============================================================

    public IReadOnlyList<VinCell> Cells { get; } = Enumerable.Range(0, 17).Select(i => new VinCell(i)).ToList();

    [ObservableProperty] private bool isEditing;

    public string CountText => $"{Vin.Length} / 17";
    public bool HasText => Vin.Length > 0;

    partial void OnIsEditingChanged(bool value) => UpdateCells();

    private void UpdateCells()
    {
        for (var i = 0; i < Cells.Count; i++)
        {
            Cells[i].Text = i < Vin.Length ? Vin[i].ToString() : "";
            Cells[i].IsCurrent = IsEditing && i == Math.Min(Vin.Length, 16);
        }
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasText));
    }

    [RelayCommand]
    private void Clear() => Vin = "";

    /// <summary>Opens the camera; the recognised VIN is written into the boxes.</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        VinScanBridge.Request(vin => MainThread.BeginInvokeOnMainThread(() => Vin = vin));
        try
        {
            await Shell.Current.GoToAsync(nameof(VinScanPage));
        }
        catch
        {
            ErrorMessage = VinText.T("ScanCameraError");
        }
    }

    partial void OnVinChanged(string value)
    {
        var clean = validator.Normalize(value);
        if (clean != value)
            Vin = clean;
        ErrorMessage = "";
        SearchCommand.NotifyCanExecuteChanged();
        UpdateCells();
    }

    private bool CanSearch() => !IsLoading && Vin.Length == 17;

    partial void OnIsLoadingChanged(bool value) => SearchCommand.NotifyCanExecuteChanged();

    /// <summary>Fills in the VIN read from the car when the field is still empty.</summary>
    public void PrefillFromConnectedCar()
    {
        try
        {
            var carVin = vehicleInfo.CurrentVehicle?.VIN;
            if (string.IsNullOrWhiteSpace(Vin) && validator.IsValid(carVin, out _))
                Vin = carVin!.Trim().ToUpperInvariant();
        }
        catch
        {
        }
    }

    /// <summary>Texts follow the app language (also after it was changed elsewhere).</summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (!validator.IsValid(Vin, out var error)) { ErrorMessage = error; return; }
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet && vinService is not MockVinHistoryService)
        {
            ErrorMessage = VinText.T("NoInternet");
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var report = await vinService.GetReportAsync(Vin, _searchCts.Token);
            await database.SaveSearchAsync(report);
            await Shell.Current.GoToAsync(nameof(VinReportPage), new ShellNavigationQueryParameters { { "Report", report } });
        }
        catch (OperationCanceledException) { }
        catch (VinServiceException ex)
        {
            ErrorMessage = ex.Kind switch
            {
                VinError.NotFound => VinText.T("NotFound"),
                VinError.Offline => VinText.T("NoInternet"),
                VinError.RateLimited => VinText.T("TooMany"),
                _ => VinText.T("Unavailable")
            };
        }
        catch (Exception) { ErrorMessage = VinText.T("Unavailable"); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private Task OpenHistoryAsync() => Shell.Current.GoToAsync(nameof(VinHistoryPage));

    [RelayCommand]
    private void ToggleLanguage()
    {
        VinText.Toggle();
        RefreshTexts();
    }

    [RelayCommand]
    private Task BackAsync() => Shell.Current.GoToAsync("..");

    public void Cancel() => _searchCts?.Cancel();
}

/// <summary>One box of the VIN input.</summary>
public partial class VinCell(int index) : ObservableObject
{
    public int Index { get; } = index;

    [ObservableProperty] private string text = "";
    [ObservableProperty] private bool isCurrent;
}
