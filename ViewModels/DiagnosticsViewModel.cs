using CarScanner.Models;
using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CarScanner.ViewModels;

public partial class DiagnosticsViewModel : BaseViewModel
{
    private readonly DiagnosticService _diagnostic;
    private readonly DatabaseService _db;
    private readonly ReminderNotificationService _notifications;
    private readonly OBDService _obd;
    private readonly VehicleInfoService _vehicle;
    private readonly LocalizationService _localization;
    private readonly RepairCostSession _repairCosts;

    public ObservableCollection<ErrorCode> Errors { get; } = new();

    [ObservableProperty]
    private string carInfo = "უცნობი მანქანა";

    [ObservableProperty]
    private string vin = string.Empty;

    [ObservableProperty]
    private bool hasErrors;

    [ObservableProperty]
    private string protocol = string.Empty;

    [ObservableProperty]
    private string readiness = string.Empty;

    [ObservableProperty]
    private string freezeFrame = string.Empty;
    public ObservableCollection<FreezeFrameValue> FreezeFrameValues { get; } = new();
    [ObservableProperty] private bool hasFreezeFrameData;
    [ObservableProperty] private bool isFreezeFrameLoading;
    [ObservableProperty] private int freezeFrameProgress;
    [ObservableProperty] private string freezeFrameProgressText = "Freeze Frame ჯერ არ წაკითხულა.";
    [ObservableProperty] private string freezeFrameTitle = "Freeze Frame — ხარვეზის მომენტის მონაცემები";
    [ObservableProperty] private bool showFreezeFrame;
    [ObservableProperty] private string freezeFrameEmptyMessage = "წაკითხვის შემდეგ აქ გამოჩნდება მხოლოდ ECU-ის მიერ რეალურად შენახული snapshot.";
    public double FreezeFrameProgressValue => FreezeFrameProgress / 100d;

    partial void OnFreezeFrameProgressChanged(int value) => OnPropertyChanged(nameof(FreezeFrameProgressValue));
    [ObservableProperty] private string scanSummary = "Quick Scan მზადაა";
    [ObservableProperty] private string currentCount = "0 Current";
    [ObservableProperty] private string historyCount = "0 History";
    [ObservableProperty] private string modulesSummary = "ECM ჯერ არ არის დასკანერებული";
    [ObservableProperty] private int scanProgress;
    [ObservableProperty] private string scanProgressText = "სკანირება მზადაა";
    [ObservableProperty] private bool isDtcScanInProgress;
    [ObservableProperty] private string emptyStateTitle = "შეცდომის კოდი არ არის";
    [ObservableProperty] private string emptyStateMessage = "პირველ რიგში შეასრულე სკანირება.";

    public double ScanProgressValue => ScanProgress / 100d;
    public string ScanProgressPercent => $"{ScanProgress}%";

    partial void OnScanProgressChanged(int value)
    {
        OnPropertyChanged(nameof(ScanProgressValue));
        OnPropertyChanged(nameof(ScanProgressPercent));
    }

    public DiagnosticsViewModel(
        DiagnosticService diagnostic,
        DatabaseService db,
        OBDService obd,
        VehicleInfoService vehicle,
        ReminderNotificationService notifications,
        LocalizationService localization,
        RepairCostSession repairCosts)
    {
        _diagnostic = diagnostic;
        _db = db;
        _obd = obd;
        _vehicle = vehicle;
        _notifications = notifications;
        _localization = localization;
        _repairCosts = repairCosts;

        Title = "დიაგნოსტიკა";

        _localization.LanguageChanged += OnLanguageChanged;
    }

    // ============================================================
    // LANGUAGE
    // ============================================================

    private void OnLanguageChanged(
        object? sender,
        EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var item in Errors)
            {
                item.OnChanged();
            }

            var copy = Errors.ToList();

            Errors.Clear();

            foreach (var item in copy)
            {
                Errors.Add(item);
            }
        });
    }

    // ============================================================
    // DTC SCAN
    // ============================================================

    [RelayCommand]
    private async Task Scan()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            IsDtcScanInProgress = true;

            Errors.Clear();
            HasErrors = false;
            FreezeFrame = "Freeze Frame-ის მონაცემები ჯერ არ არის წაკითხული.";
            FreezeFrameValues.Clear();
            HasFreezeFrameData = false;
            FreezeFrameTitle = "Freeze Frame — ხარვეზის მომენტის მონაცემები";
            FreezeFrameEmptyMessage = "DTC სკანირების შემდეგ შეგიძლია ECU-ის შენახული snapshot წაიკითხო.";
            EmptyStateTitle = "სკანირება მიმდინარეობს";
            EmptyStateMessage = "ECU-ის რეალური პასუხების მოლოდინი…";
            StatusMessage = "DTC სკანირება მიმდინარეობს…";

            UpdateScanProgress(1, "OBD კავშირის შემოწმება…");

            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();

            Protocol =
                _obd.CurrentProtocolDescription;

            UpdateScanProgress(10, "OBD პროტოკოლი მზადაა.");

            // ----------------------------------------------------
            // READINESS
            // ----------------------------------------------------

            UpdateScanProgress(10, "MIL / Readiness სტატუსის წაკითხვა…");
            try
            {
                var ready =
                    await _obd.ReadReadinessAsync();

                Readiness =
                    ready.MilOn
                        ? $"MIL: ჩართულია — ECU ითხოვს Check Engine ინდიკატორს. შენახული DTC: {ready.StoredDtcCount}."
                        : $"MIL: გამორთულია — ECU ამ მომენტში არ ითხოვს Check Engine ინდიკატორს. შენახული DTC: {ready.StoredDtcCount}.";
            }
            catch
            {
                Readiness =
                    "MIL / Readiness სტატუსი ECU-დან მიუწვდომელია.";
            }

            UpdateScanProgress(25, "MIL / Readiness სტატუსი დამუშავდა.");

            // ----------------------------------------------------
            // VIN
            // ----------------------------------------------------

            UpdateScanProgress(25, "VIN და Vehicle Info-ის წაკითხვა…");

            // The connection flow already retrieves this real vehicle
            // profile. Reusing it avoids issuing VIN and fuel-type commands
            // again for every DTC scan; read it only when it is not present.
            var info = _vehicle.CurrentVehicle
                ?? await _vehicle.IdentifyConnectedVehicleAsync();

            Vin =
                info.VIN;

            CarInfo =
                info.Manufacturer +
                (
                    info.Year != "Unknown"
                        ? $" {info.Year}"
                        : string.Empty
                );

            UpdateScanProgress(35, "Vehicle Info მიღებულია.");

            // ----------------------------------------------------
            // DTC
            // ----------------------------------------------------

            var progress = new Progress<DtcScanProgress>(stage =>
                UpdateScanProgress(stage.Percent, stage.Stage));

            var list =
                await _diagnostic.ScanForErrorsAsync(progress);

            foreach (var error in list)
            {
                Errors.Add(error);
            }

            HasErrors =
                Errors.Count > 0;

            var current = Errors.Count(x => x.Status is "შენახული" or "მოლოდინში");
            var history = Errors.Count(x => x.Status == "მუდმივი");
            CurrentCount = $"{current} Current";
            HistoryCount = $"{history} Permanent/History";
            ModulesSummary = "Generic Engine / Powertrain ECU დასკანერებულია";
            ScanSummary = HasErrors ? $"Quick Scan: {Errors.Count} unique DTC" : "აქტიური DTC არ აღმოჩნდა.";

            var completionMessage =
                HasErrors
                    ? $"ნაპოვნია {Errors.Count} კოდი"
                    : "აქტიური შეცდომის კოდები არ აღმოჩნდა — დიაგნოსტიკის მიხედვით პრობლემა არ დაფიქსირდა.";

            if (HasErrors)
            {
                EmptyStateTitle = "";
                EmptyStateMessage = "";
            }
            else
            {
                EmptyStateTitle = "სკანირება წარმატებით დასრულდა";
                EmptyStateMessage = "აქტიური შეცდომის კოდები არ აღმოჩნდა — დიაგნოსტიკის მიხედვით პრობლემა არ დაფიქსირდა.";
            }

            UpdateScanProgress(100, "DTC სკანირება დასრულდა.");
            StatusMessage = completionMessage;

            // ----------------------------------------------------
            // SAVE SCAN HISTORY
            // ----------------------------------------------------

            await _db.SaveScanAsync(
                new ScanResult
                {
                    ScanDate =
                        DateTime.Now,

                    CarInfo =
                        CarInfo,

                    FoundCodes =
                        string.Join(
                            ", ",
                            Errors.Select(
                                x => x.Code)),

                    Notes =
                        StatusMessage +
                        " | VIN: " +
                        Vin
                });

            await _notifications
                .MarkScanCompletedAsync();
        }
        catch (Exception ex)
        {
            StatusMessage =
                ex.Message;
            EmptyStateTitle = "სკანირება ვერ დასრულდა";
            EmptyStateMessage = "შეამოწმე OBD ადაპტერი, მანქანის კონტაქტი და სცადე თავიდან.";
        }
        finally
        {
            IsDtcScanInProgress = false;
            IsBusy = false;
        }
    }

    // ============================================================
    // FREEZE FRAME
    // ============================================================

    [RelayCommand]
    private async Task ReadFreezeFrame()
    {
        ShowFreezeFrame = true;
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            IsFreezeFrameLoading = true;
            FreezeFrameProgress = 0;
            FreezeFrameProgressText = "OBD / Freeze Frame მზადდება…";
            FreezeFrameValues.Clear();
            HasFreezeFrameData = false;

            StatusMessage =
                "Freeze Frame იკითხება...";

            if (!_obd.IsInitialized)
                await _obd.InitializeAsync();

            var code =
                Errors
                    .FirstOrDefault()
                    ?.Code;

            var progress = new Progress<(int Percent, string Stage)>(step =>
            {
                FreezeFrameProgress = step.Percent;
                FreezeFrameProgressText = step.Stage;
            });
            var ff = await _obd.ReadFreezeFrameDetailedAsync(code, progress);

            if (ff.Values.Count == 0)
            {
                FreezeFrame = "Freeze Frame მონაცემი ECU-ში არ არის შენახული ან ამ ECU-ზე მიუწვდომელია.";
                FreezeFrameTitle = "Freeze Frame მიუწვდომელია";
                FreezeFrameEmptyMessage = "ამ ECU-მ Mode 02 Freeze Frame DTC არ დააბრუნა. ეს ხშირად ნორმალურია, როცა აქტიური/შენახული ხარვეზი არ არსებობს.";
                FreezeFrameProgress = 100;
                FreezeFrameProgressText = "Freeze Frame მონაცემი ECU-ში არ აღმოჩნდა.";
                StatusMessage = "Freeze Frame მონაცემი ECU-ში არ არის შენახული.";
            }
            else
            {
                FreezeFrame = string.Join(
                    "\n",
                    ff.Values.Select(x => $"{x.Key}: {x.Value}"));
                foreach (var item in ff.Values)
                    FreezeFrameValues.Add(new FreezeFrameValue { Label = item.Key, Value = item.Value });
                HasFreezeFrameData = true;
                FreezeFrameTitle = string.IsNullOrWhiteSpace(ff.TriggerCode)
                    ? "Freeze Frame — რეალური ECU snapshot"
                    : $"Freeze Frame — DTC {ff.TriggerCode}";
                FreezeFrameEmptyMessage = string.Empty;
                StatusMessage = "Freeze Frame-ის რეალური მონაცემები ECU-დან წაიკითხა.";
            }
        }
        catch (Exception ex)
        {
            FreezeFrameTitle = "Freeze Frame ვერ წაიკითხა";
            FreezeFrameEmptyMessage = "შეამოწმე OBD კავშირი, ანთება და სცადე თავიდან.";
            FreezeFrameProgressText = "Freeze Frame წაკითხვა ვერ დასრულდა.";
            StatusMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "Freeze Frame წაკითხვა ვერ დაიწყო."
                : $"Freeze Frame წაკითხვა ვერ დასრულდა: {ex.Message}";
        }
        finally
        {
            IsFreezeFrameLoading = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenRelatedLiveData() => Shell.Current.GoToAsync(nameof(Views.LiveDataPage));

    [RelayCommand]
    private Task OpenFullVehicleScan() => Shell.Current.GoToAsync(nameof(Views.ModuleScanPage));

    [RelayCommand]
    private async Task OpenRepairCosts()
    {
        if (Errors.Count == 0)
            return;
        _repairCosts.Set(_vehicle.CurrentVehicle, Errors);
        await Shell.Current.GoToAsync(nameof(Views.RepairCostPage));
    }

    private void UpdateScanProgress(int percent, string stage)
    {
        ScanProgress = Math.Clamp(percent, 0, 100);
        ScanProgressText = stage;
    }

    // ============================================================
    // CLEAR DTC
    // ============================================================

    [RelayCommand]
    private async Task ClearCodes()
    {
        if (IsBusy ||
            !_obd.IsInitialized)
        {
            return;
        }

        var confirmed =
            await Shell.Current.DisplayAlertAsync(
                CarScanner.Services.Localization.LocalizationService.T("ფრთხილად"),
                CarScanner.Services.Localization.LocalizationService.T("DTC კოდების წაშლა წაშლის შენახულ დიაგნოსტიკურ ინფორმაციას. გაგრძელდეს?"),
                CarScanner.Services.Localization.LocalizationService.T("წაშლა"),
                CarScanner.Services.Localization.LocalizationService.T("გაუქმება"));

        if (!confirmed)
            return;

        try
        {
            IsBusy = true;

            StatusMessage =
                "კოდები იშლება...";

            var clear =
                await _obd.ClearDtcWithResultAsync();

            if (!clear.IsSuccess)
            {
                StatusMessage =
                    $"DTC clear ვერ დადასტურდა: {clear.Message}";

                return;
            }

            // ----------------------------------------------------
            // VERIFY CLEAR
            // ----------------------------------------------------

            var verified =
                await _obd.VerifyDtcClearAsync();

            if (
                verified.Status ==
                DiagnosticOperationStatus.ClearedSuccessfully)
            {
                Errors.Clear();

                HasErrors = false;

                StatusMessage =
                    "კოდები წაიშალა და Stored/Pending/Permanent/MIL verification წარმატებით დასრულდა.";
            }
            else
            {
                StatusMessage =
                    $"Clear verification: {verified.Message}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage =
                ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
