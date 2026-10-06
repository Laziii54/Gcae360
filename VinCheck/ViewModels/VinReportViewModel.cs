using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CarScanner.Services;
using CarScanner.Views;
using CarScanner.VinCheck.Models;
using CarScanner.VinCheck.Services;
using CarScanner.VinCheck.Views;

namespace CarScanner.VinCheck.ViewModels;

public partial class VinReportViewModel(
    IVinHistoryService service,
    IVinHistoryDatabaseService database,
    AccountService account) : ObservableObject, IQueryAttributable
{
    [ObservableProperty] private VinReport? report;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool isFavorite;
    [ObservableProperty] private bool isUnlocking;
    [ObservableProperty] private bool showAllSpecs;
    [ObservableProperty] private FullHistoryInfo? full;
    [ObservableProperty] private bool isBuyingFull;
    [ObservableProperty] private string fullStatusText = "";

    // ============================================================
    // PAID FULL HISTORY (official US title data, bank card payment)
    // ============================================================

    private string? _fullVin;
    private CancellationTokenSource? _fullWatch;

    /// <summary>Few photos / facts from the free sources.</summary>
    private bool FreeDataIsThin =>
        Report is { } r &&
        !ShowUnlockCard &&
        (ShowNoAuction || ShowHistoryUnavailable ||
         Math.Max(r.Access?.PhotoCount ?? 0, r.AuctionRecords.Sum(a => a.ImageUrls.Count)) + r.WebPhotos.Count < 3);

    public bool ShowFullOffer => Full is { Available: true, Purchased: false, Pending: false } && FreeDataIsThin;
    public bool ShowFullPending => Full is { Pending: true, Purchased: false } || !string.IsNullOrEmpty(FullStatusText);
    public bool ShowFullReport => Full is { Purchased: true, Report: not null };
    public FullHistoryReport? FullReport => Full?.Report;
    public IReadOnlyList<FullHistorySection> FullSections => Full?.Report?.Sections ?? [];
    public bool HasFullOdometer => !string.IsNullOrWhiteSpace(Full?.Report?.LastOdometer);
    public string FullOdometerText => VinText.Format("FullLastOdometer", Full?.Report?.LastOdometer ?? "");
    public Color FullSummaryColor => Full?.Report is { Clean: false } ? Color.FromArgb("#DC2626") : Color.FromArgb("#16A34A");

    public string FullTitleText => VinText.T("FullTitle");
    public string FullOfferHintText => VinText.T("FullOfferHint");
    public string FullIncludesText => VinText.T("FullIncludes");
    public string FullNoPhotosText => VinText.T("FullNoPhotosNote");
    public string FullBuyText => VinText.Format("FullBuy", (Full?.Price ?? 0).ToString("0.00", CultureInfo.InvariantCulture));
    public string FullRefreshText => VinText.T("FullRefresh");

    partial void OnFullChanged(FullHistoryInfo? value) => NotifyFull();
    partial void OnFullStatusTextChanged(string value) => OnPropertyChanged(nameof(ShowFullPending));

    private void NotifyFull()
    {
        OnPropertyChanged(nameof(ShowFullOffer));
        OnPropertyChanged(nameof(ShowFullPending));
        OnPropertyChanged(nameof(ShowFullReport));
        OnPropertyChanged(nameof(FullReport));
        OnPropertyChanged(nameof(FullSections));
        OnPropertyChanged(nameof(HasFullOdometer));
        OnPropertyChanged(nameof(FullOdometerText));
        OnPropertyChanged(nameof(FullSummaryColor));
        OnPropertyChanged(nameof(FullBuyText));
    }

    /// <summary>Loads the offer / bought report (called when the page shows).</summary>
    public async Task LoadFullAsync()
    {
        if (Report is null || !account.IsSignedIn)
            return;
        var vin = Report.Vin;
        var info = await service.GetFullHistoryAsync(vin, CancellationToken.None);
        if (Report?.Vin != vin)
            return;
        Full = info;
        if (info is { Purchased: true })
            FullStatusText = "";
        else if (info is { Pending: true })
            FullStatusText = VinText.T("FullPending");
    }

    [RelayCommand]
    private async Task BuyFullAsync()
    {
        try
        {
        if (Report is null || IsBuyingFull)
            return;

        if (!account.IsSignedIn)
        {
            await Shell.Current.DisplayAlertAsync(VinText.T("VinHistory"), VinText.T("SignInRequired"), "OK");
            try { await Shell.Current.GoToAsync(nameof(AccountPage)); } catch { }
            return;
        }

        IsBuyingFull = true;
        FullStatusText = VinText.T("FullOpening");
        try
        {
            var result = await service.StartFullCheckoutAsync(Report.Vin, CancellationToken.None);
            if (result.Purchased)
            {
                FullStatusText = "";
                await LoadFullAsync();
                return;
            }

            if (!result.Success || string.IsNullOrWhiteSpace(result.RedirectUrl))
            {
                FullStatusText = "";
                var message = result.Code switch
                {
                    "NO_RECORDS" => VinText.T("FullNoRecords"),
                    "SIGN_IN" => VinText.T("SignInRequired"),
                    "OFFLINE" => VinText.T("NoInternet"),
                    "TOO_MANY" => VinText.T("TooMany"),
                    _ => VinText.T("FullFailed")
                };
                await Shell.Current.DisplayAlertAsync(FullTitleText, message, "OK");
                await LoadFullAsync();
                return;
            }

            FullStatusText = VinText.T("FullWaiting");
            try
            {
                await Browser.Default.OpenAsync(new Uri(result.RedirectUrl), BrowserLaunchMode.SystemPreferred);
            }
            catch
            {
                await Launcher.Default.OpenAsync(new Uri(result.RedirectUrl));
            }

            WatchFullPayment();
        }
        finally
        {
            IsBuyingFull = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinReportViewModel.BuyFullAsync");
        }
    }

    /// <summary>Checks every few seconds until the paid report arrives (max ~10 min).</summary>
    private void WatchFullPayment()
    {
        _fullWatch?.Cancel();
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        _fullWatch = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                    await MainThread.InvokeOnMainThreadAsync(LoadFullAsync);
                    if (Full is { Purchased: true })
                        break;
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (Full is not { Purchased: true, Report: not null })
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (Full is not { Pending: true })
                            FullStatusText = "";
                    });
            }
        });
    }

    [RelayCommand]
    private Task RefreshFullAsync() => LoadFullAsync();

    /// <summary>Stops the payment check when the page is left.</summary>
    public void StopWatching() => _fullWatch?.Cancel();

    // ============================================================
    // HEADER / SUMMARY
    // ============================================================

    /// <summary>Short facts under the car name (only the known ones).</summary>
    public IReadOnlyList<string> HeroChips =>
        Report is null
            ? []
            : new[] { Report.Year?.ToString(), Report.Trim, Report.Engine, Report.FuelType, Report.BodyType, Report.DriveType, Report.Transmission }
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!.Length > 34 ? text[..34].TrimEnd() + "…" : text)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>Brand logo from Resources/Images/brands (null when there is none).</summary>
    public string? BrandLogo => new BrandLogoConverter().Convert(Report?.Make, typeof(string), null, CultureInfo.InvariantCulture) as string;
    public bool HasBrandLogo => BrandLogo is not null;

    private const int SpecPreviewCount = 6;

    public IReadOnlyList<VehicleSpecification> VisibleSpecs =>
        Report is null
            ? []
            : ShowAllSpecs ? Report.Specifications : Report.Specifications.Take(SpecPreviewCount).ToList();

    public bool HasMoreSpecs => Report is { } r && r.Specifications.Count > SpecPreviewCount;
    public string SpecsToggleText => VinText.T(ShowAllSpecs ? "ShowLess" : "ShowAll");

    partial void OnShowAllSpecsChanged(bool value)
    {
        OnPropertyChanged(nameof(VisibleSpecs));
        OnPropertyChanged(nameof(SpecsToggleText));
    }

    [RelayCommand]
    private void ToggleSpecs() => ShowAllSpecs = !ShowAllSpecs;

    [RelayCommand]
    private void ToggleRecall(RecallRecord? recall)
    {
        if (recall is not null)
            recall.IsExpanded = !recall.IsExpanded;
    }

    public string WebPhotosTitleText => VinText.T("WebPhotosTitle");
    public string WebPhotosHintText => VinText.T("WebPhotosHint");
    public string SearchGoogleText => VinText.T("SearchGoogle");
    public string SourcesTitleText => VinText.T("SourcesTitle");

    public bool HasWebPhotos => Report?.WebPhotos is { Count: > 0 };
    public bool HasSources => Report?.Sources is { Count: > 0 };

    /// <summary>Web photos as gallery items (tap opens the big picture).</summary>
    public IReadOnlyList<PhotoThumb> WebThumbs
    {
        get
        {
            if (Report?.WebPhotos is not { Count: > 0 } photos)
                return [];
            var full = photos.Select(p => string.IsNullOrWhiteSpace(p.ImageUrl) ? p.ThumbnailUrl : p.ImageUrl).ToList();
            return photos.Select((p, i) => new PhotoThumb
            {
                Url = p.ThumbnailUrl,
                Overlay = p.Source ?? "",
                Selection = new PhotoSelection { Images = full, Index = i }
            }).ToList();
        }
    }

    [RelayCommand]
    private async Task SearchGoogleAsync()
    {
        try
        {
        if (Report is null) return;
        var url = Report.WebSearchUrl ?? "https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(Report.Vin);
        try { await Launcher.Default.OpenAsync(new Uri(url)); } catch { }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinReportViewModel.SearchGoogleAsync");
        }
    }

    public string CustomsText => Microsoft.Maui.Storage.Preferences.Default.Get("language", "English") == "English"
        ? "🧮 Customs cost in Georgia"
        : "🧮 განბაჟების გამოთვლა";

    /// <summary>Opens the customs calculator with year, engine size and fuel from this report.</summary>
    [RelayCommand]
    private async Task OpenCustomsAsync()
    {
        try
        {
        if (Report is null)
            return;

        string? Spec(string key) => Report.Specifications.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
        var cc = decimal.TryParse(Spec("DisplacementL"), NumberStyles.Number, CultureInfo.InvariantCulture, out var litres) && litres > 0
            ? litres.ToString("0.0#", CultureInfo.InvariantCulture)
            : "";
        var fuelText = $"{Report.FuelType} {Spec("ElectrificationLevel")} {Report.Engine}".ToLowerInvariant();
        var fuel = fuelText.Contains("plug-in") || fuelText.Contains("plug in") || fuelText.Contains("phev") ? "phev"
            : fuelText.Contains("hybrid") || fuelText.Contains("hev") ? "hybrid"
            : fuelText.Contains("electric") && !fuelText.Contains("gasoline") && !fuelText.Contains("petrol") ? "electric"
            : fuelText.Contains("diesel") ? "diesel"
            : "petrol";
        var year = Report.Year?.ToString(CultureInfo.InvariantCulture) ?? "";

        try
        {
            await Shell.Current.GoToAsync($"{nameof(CustomsCalculatorPage)}?year={year}&litres={cc}&fuel={fuel}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Customs navigation failed: {ex.Message}");
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinReportViewModel.OpenCustomsAsync");
        }
    }

    public string NoAuctionTitleText => VinText.T("NoAuctionTitle");
    public string NoAuctionHintText => VinText.T("NoAuctionHint");
    public string RecallHintText => VinText.T("RecallHint");
    public string AtAGlanceText => VinText.T("AtAGlance");
    public string SoldForText => VinText.T("SoldFor");
    public string LotLabel => VinText.T("Lot");
    public string SaleLabel => VinText.T("Sale");
    public string PrimaryLabel => VinText.T("Primary");
    public string SecondaryLabel => VinText.T("Secondary");
    public string OdometerLabel => VinText.T("Odometer");
    public string TitleLabel => VinText.T("Title");
    public string KeysLabel => VinText.T("Keys");
    public string RunsLabel => VinText.T("RunsDrives");

    // ============================================================
    // LOCKED HISTORY (free preview) / VIN ALLOWANCE
    // ============================================================

    private VinAccessInfo? Access => Report?.Access;

    /// <summary>The blurred card with the "open full history" button.</summary>
    public bool ShowUnlockCard =>
        Access is { Unlocked: false } access &&
        (access.HistoryLocked || access.HistoryState == "deferred");

    public bool ShowHistoryUnavailable =>
        Access is { Unlocked: false, HistoryState: "unavailable" };

    public bool ShowNoAuction =>
        Access is { HistoryState: "ready", HistoryLocked: false, AuctionCount: 0 };

    public string LockedTitleText => VinText.T("LockedTitle");
    public string LockedHintText => VinText.T("LockedHint");
    public string UnlockButtonText => VinText.T("UnlockButton");
    public string UnlockingText => VinText.T("Unlocking");
    public string HistoryUnavailableText => VinText.T("HistoryUnavailable");
    public string NoAuctionText => VinText.T("NoAuctionFound");

    public string LockedFoundText =>
        Access is { HistoryLocked: true } access
            ? access.PhotoCount > 0
                ? VinText.Format("LockedFound", access.AuctionCount, access.PhotoCount)
                : VinText.Format("LockedFoundNoCount", access.AuctionCount)
            : VinText.T("LockedDeferred");

    public bool ShowQuota => !string.IsNullOrEmpty(QuotaText);

    public string QuotaText
    {
        get
        {
            if (Access is not { } access)
                return string.Empty;

            var parts = new List<string>();
            if (access.HasVinPlan)
                parts.Add(VinText.Format("QuotaLeft", access.Remaining, access.MonthlyLimit));
            if (access.Credits > 0)
                parts.Add(VinText.Format("QuotaCredits", access.Credits));
            return string.Join(" · ", parts);
        }
    }

    partial void OnReportChanged(VinReport? value)
    {
        OnPropertyChanged(nameof(ShowUnlockCard));
        OnPropertyChanged(nameof(ShowHistoryUnavailable));
        OnPropertyChanged(nameof(ShowNoAuction));
        OnPropertyChanged(nameof(LockedFoundText));
        OnPropertyChanged(nameof(QuotaText));
        OnPropertyChanged(nameof(ShowQuota));
        OnPropertyChanged(nameof(HeroChips));
        OnPropertyChanged(nameof(HasWebPhotos));
        OnPropertyChanged(nameof(WebThumbs));
        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(BrandLogo));
        OnPropertyChanged(nameof(HasBrandLogo));
        OnPropertyChanged(nameof(VisibleSpecs));
        OnPropertyChanged(nameof(HasMoreSpecs));

        if (value?.Vin != _fullVin)
        {
            _fullVin = value?.Vin;
            _fullWatch?.Cancel();
            FullStatusText = "";
            Full = null;
            _ = LoadFullAsync();
        }
        NotifyFull();
    }

    public string ReportTitle => VinText.T("Report");
    public string CopyVinText => VinText.T("CopyVin");
    public string ShareText => VinText.T("Share");
    public string MakeLabel => VinText.T("Make");
    public string ModelLabel => VinText.T("Model");
    public string YearLabel => VinText.T("Year");
    public string TrimLabel => VinText.T("Trim");
    public string EngineLabel => VinText.T("Engine");
    public string FuelLabel => VinText.T("Fuel");
    public string TransmissionLabel => VinText.T("Transmission");
    public string DrivetrainLabel => VinText.T("Drivetrain");
    public string VehicleSummaryText => VinText.T("VehicleSummary");
    public string ImportantHistoryText => VinText.T("ImportantHistory");
    public string AuctionHistoryText => VinText.T("AuctionHistory");
    public string AccidentHistoryText => VinText.T("AccidentHistory");
    public string MileageHistoryText => VinText.T("MileageHistory");
    public string TitleHistoryText => VinText.T("TitleHistory");
    public string OwnershipHistoryText => VinText.T("OwnershipHistory");
    public string ServiceHistoryText => VinText.T("ServiceHistory");
    public string RecallsText => VinText.T("Recalls");
    public string SpecificationsText => VinText.T("Specifications");
    public string NoHistoryText => VinText.T("NoHistory");

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Report", out var value) && value is VinReport r)
        {
            VinReportPresenter.Prepare(r);
            Report = r;
            _ = LoadFavoriteAsync();
        }
    }

    private async Task LoadFavoriteAsync()
    {
        if (Report is not null)
            IsFavorite = await database.IsFavoriteAsync(Report.Vin);
    }

    [RelayCommand]
    private async Task CopyVinAsync()
    {
        try
        {
        if (Report is null) return;
        await Clipboard.Default.SetTextAsync(Report.Vin);
        await Shell.Current.DisplayAlertAsync(VinText.T("VinHistory"), VinText.T("VinCopied"), "OK");
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinReportViewModel.CopyVinAsync");
        }
    }

    [RelayCommand]
    private async Task ShareAsync()
    {
        try
        {
        if (Report is null) return;
        var text = $"{Report.Year} {Report.DisplayName}\nVIN: {Report.Vin}";
        if (!string.IsNullOrWhiteSpace(Report.ReportUrl))
            text += $"\n{Report.ReportUrl}";
        await Share.Default.RequestAsync(new ShareTextRequest { Title = Report.DisplayName, Text = text });
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinReportViewModel.ShareAsync");
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        try
        {
        if (Report is not null)
            IsFavorite = await database.ToggleFavoriteAsync(Report);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinReportViewModel.ToggleFavoriteAsync");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
        if (Report is null || IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            Report = await service.GetReportAsync(Report.Vin, CancellationToken.None);
            await database.SaveSearchAsync(Report);
        }
        catch
        {
            await Shell.Current.DisplayAlertAsync(VinText.T("VinHistory"), VinText.T("Unavailable"), "OK");
        }
        finally
        {
            IsRefreshing = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinReportViewModel.RefreshAsync");
        }
    }

    // ============================================================
    // UNLOCK
    // ============================================================

    [RelayCommand]
    private async Task UnlockAsync()
    {
        try
        {
        if (Report is null || IsUnlocking)
            return;

        // Opened reports and purchases are kept on the account, so the
        // user signs in first (nothing is charged before that).
        if (!account.IsSignedIn)
        {
            await Shell.Current.DisplayAlertAsync(VinText.T("VinHistory"), VinText.T("SignInRequired"), "OK");
            try { await Shell.Current.GoToAsync(nameof(AccountPage)); } catch { }
            return;
        }

        var access = Report.Access;
        if (access is { HasVinPlan: false, Credits: 0 })
        {
            await OfferPlansAsync();
            return;
        }

        if (access is { HasVinPlan: true, Remaining: <= 0, Credits: <= 0 })
        {
            await OfferPacksAsync(access.MonthlyLimit, access.ResetsAt);
            return;
        }

        await OpenHistoryAsync();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinReportViewModel.UnlockAsync");
        }
    }

    private async Task OpenHistoryAsync()
    {
        if (Report is null)
            return;

        IsUnlocking = true;
        try
        {
            var opened = await service.UnlockAsync(Report.Vin, CancellationToken.None);
            Report = opened;
            await database.SaveSearchAsync(opened);

            if (opened.Access is { AuctionCount: 0 })
                await Shell.Current.DisplayAlertAsync(VinText.T("VinHistory"), VinText.T("NotCharged"), "OK");
        }
        catch (VinServiceException ex) when (ex.Kind == VinError.PlanRequired)
        {
            await OfferPlansAsync();
        }
        catch (VinServiceException ex) when (ex.Kind == VinError.QuotaExceeded)
        {
            await OfferPacksAsync(ex.Quota?.MonthlyLimit ?? ProService.VinMonthlyReports, ex.Quota?.ResetsAt);
        }
        catch (VinServiceException ex)
        {
            var message = ex.Kind switch
            {
                VinError.SignInRequired => VinText.T("SignInRequired"),
                VinError.Offline => VinText.T("NoInternet"),
                VinError.RateLimited => VinText.T("TooMany"),
                _ => VinText.T("HistoryUnavailable")
            };
            await Shell.Current.DisplayAlertAsync(VinText.T("VinHistory"), message, "OK");
        }
        catch (Exception)
        {
            await Shell.Current.DisplayAlertAsync(VinText.T("VinHistory"), VinText.T("HistoryUnavailable"), "OK");
        }
        finally
        {
            IsUnlocking = false;
        }
    }

    /// <summary>No VIN plan and no extra checks on the account.</summary>
    private static Task OfferPlansAsync() =>
        Shell.Current.DisplayAlertAsync(
            VinText.T("PlanRequiredTitle"),
            VinText.T("PlanRequiredMessage"),
            "OK");

    /// <summary>All monthly checks used and no extra checks left.</summary>
    private static Task OfferPacksAsync(int monthlyLimit, DateTimeOffset? resetsAt)
    {
        var message = resetsAt is { } reset
            ? VinText.Format("QuotaReset", monthlyLimit, reset.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture))
            : string.Empty;

        return Shell.Current.DisplayAlertAsync(
            VinText.Format("QuotaUsedTitle", monthlyLimit),
            message,
            "OK");
    }

    [RelayCommand]
    private async Task OpenAuctionPhotoAsync(PhotoSelection selection)
    {
        try
        {
        if (selection.Images.Count == 0) return;
        await Shell.Current.GoToAsync(nameof(VinPhotoGalleryPage), new ShellNavigationQueryParameters { { "Selection", selection } });
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinReportViewModel.OpenAuctionPhotoAsync");
        }
    }
}

public sealed class PhotoSelection
{
    public IReadOnlyList<string> Images { get; init; } = [];
    public int Index { get; init; }
}
