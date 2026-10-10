using CarScanner.Services;
using CarScanner.Services.DataCollection;
using CarScanner.Services.Localization;
using CarScanner.Services.OemDiagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;

namespace CarScanner.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly IBluetoothService _bluetooth;
    private readonly DatabaseService _db;
    private readonly SettingsService _settings;
    private readonly UnitSystemService _units;
    private readonly LocalizationService _localization;
    private readonly ReminderNotificationService _notifications;
    private readonly DataCollectionService _dataCollection;
    private readonly LocalCaptureService _capture;
    private IReadOnlyList<CaptureTarget> _captureTargets = Array.Empty<CaptureTarget>();
    private bool _initialized;
    private bool _syncingConsent;

    [ObservableProperty]
    private string bluetoothStatus = "უცნობი";

    [ObservableProperty]
    private string themeOption = "Light";

    [ObservableProperty]
    private string languageOption = "English";

    [ObservableProperty]
    private bool notificationsEnabled;

    [ObservableProperty]
    private bool autoReconnect = true;

    [ObservableProperty]
    private bool dataCollectionEnabled;

    [ObservableProperty]
    private string[] captureTargetOptions = Array.Empty<string>();

    [ObservableProperty]
    private int selectedCaptureTargetIndex;

    [ObservableProperty]
    private bool captureRecording;

    [ObservableProperty]
    private string captureStatus = string.Empty;

    [ObservableProperty]
    private string unitOption = "Metric";

    [ObservableProperty]
    private string pollingOption = "Balanced";

    public string[] ThemeOptions { get; } =
    {
        "Dark",
        "Light",
        "System"
    };

    // ქართული, English, Türkçe, Deutsch, Polski, Español, Português, Français, Italiano
    public string[] LanguageOptions { get; } = LanguageCatalog.DisplayNames;

    public string[] UnitOptions { get; } =
    {
        "Metric",
        "Imperial"
    };

    public string[] PollingOptions { get; } =
    {
        "Fast",
        "Balanced",
        "Low power"
    };

    public SettingsViewModel(
        IBluetoothService bluetooth,
        DatabaseService db,
        SettingsService settings,
        UnitSystemService units,
        LocalizationService localization,
        ReminderNotificationService notifications,
        DataCollectionService dataCollection,
        LocalCaptureService capture)
    {
        _bluetooth = bluetooth;
        _db = db;
        _settings = settings;
        _units = units;
        _localization = localization;
        _notifications = notifications;
        _dataCollection = dataCollection;
        _capture = capture;

        Title = "Settings";

        ThemeOption =
            Preferences.Default.Get(
                "theme",
                "Light");

        LanguageOption = LanguageCatalog.Current.DisplayName;

        NotificationsEnabled =
            Preferences.Default.Get(
                "notifications",
                false);

        DataCollectionEnabled = _dataCollection.IsEnabled;
        CaptureRecording = _capture.IsRecording;
        CaptureTargetOptions = new[] { OtherVehicleOption };
        _ = LoadCaptureTargetsAsync();

        AutoReconnect =
            Preferences.Default.Get(
                "auto_reconnect",
                true);

        UnitOption = _units.DisplayName;

        PollingOption =
            Preferences.Default.Get(
                "polling",
                "Balanced");

        UpdateStatus();
        ApplyTheme();
        _initialized = true;

        // The language can also be changed on the home page or VIN pages;
        // keep this (singleton) view model in sync so the picker never
        // pushes an old language back.
        _localization.LanguageChanged += (_, __) =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!string.Equals(LanguageOption, _localization.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
                    LanguageOption = _localization.CurrentLanguage;
                UpdateStatus();
            });

        _bluetooth.ConnectionChanged +=
            (_, __) =>
                MainThread.BeginInvokeOnMainThread(
                    UpdateStatus);
    }

    private void UpdateStatus()
    {
        BluetoothStatus =
            _localization.IsEnglish
                ? (_bluetooth.IsConnected
                    ? "Connected"
                    : "Not connected")
                : (_bluetooth.IsConnected
                    ? "დაკავშირებულია"
                    : "დაკავშირება არ არის");
    }

    partial void OnThemeOptionChanged(string value)
    {
        Preferences.Default.Set(
            "theme",
            value);

        ApplyTheme(force: _initialized);

        // The launcher icon follows the theme only after an explicit change
        // here; it is switched when the app goes to the background.
        if (_initialized)
            AppIconSwitcher.RequestSwitch();

        _localization.ApplyCurrentPage();
    }

    partial void OnLanguageOptionChanged(string value)
    {
        // Already the app language (e.g. synced after the home KA/EN button):
        // nothing to switch, and never switch back to an old value.
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, _localization.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatus();
            return;
        }

        _localization.SetLanguage(value);

        UpdateStatus();
    }

    partial void OnNotificationsEnabledChanged(bool value)
    {
        _ = _notifications.SetEnabledAsync(value);
    }

    public string DataCollectionTitle =>
        _localization.IsEnglish ? "Share car compatibility data (optional)" : "მანქანის თავსებადობის მონაცემების გაზიარება (არასავალდებულო)";

    public string DataCollectionInfo =>
        _localization.IsEnglish
            ? "Off by default. If you turn it on, the app sends which standard OBD2 requests and modules your car answers (supported PIDs, trouble-code lists, module answers) — never your VIN, name, account or location. For standard OBD2 requests no measured values are sent; for individual car modules the raw read-only reply is sent, and it can contain a value the module reported at that moment. A random device code is kept so repeated reports from one phone are not counted twice; that makes the data pseudonymous, not fully anonymous. Nothing is written to your car. Turn it off at any time: queued data is deleted and the server is asked to delete what this device sent."
            : "ნაგულისხმევად გამორთულია. ჩართვისას აპი გზავნის, რომელ სტანდარტულ OBD2 მოთხოვნებსა და მოდულებს პასუხობს თქვენი მანქანა (მხარდაჭერილი PID-ები, შეცდომის კოდების სია, მოდულების პასუხები) — არასდროს VIN-ს, სახელს, ანგარიშს ან ლოკაციას. სტანდარტულ OBD2 მოთხოვნებზე გაზომილი მნიშვნელობები არ იგზავნება; ცალკეული მოდულებისგან იგზავნება ნედლი მხოლოდ-წაკითხვის პასუხი, რომელიც შეიძლება შეიცავდეს იმ წამს მოდულის მიერ დაბრუნებულ მნიშვნელობას. ინახება შემთხვევითი მოწყობილობის კოდი, რომ ერთი ტელეფონის განმეორებითი გაგზავნა ორჯერ არ დაითვალოს; ამიტომ მონაცემები ფსევდონიმურია და არა სრულად ანონიმური. მანქანაში არაფერი იწერება. ნებისმიერ დროს გამორთე: რიგი წაიშლება და სერვერს მოეთხოვება ამ მოწყობილობის მონაცემების წაშლა.";

    private string OtherVehicleOption =>
        _localization.IsEnglish ? "Other vehicle (no target)" : "სხვა მანქანა (სამიზნის გარეშე)";

    public string CaptureTitle =>
        _localization.IsEnglish ? "Record a car capture to a file (local only)" : "მანქანის ჩანაწერი ფაილში (მხოლოდ ტელეფონზე)";

    public string CaptureInfo =>
        _localization.IsEnglish
            ? "Records the read-only requests and replies the app exchanges with the car while you run a scan, and saves them as a file you can share. Nothing is uploaded by the app. No VIN or serial numbers are kept, but a module reply can contain a value the module reported at that moment. A recording is raw material only: it does not make any feature supported. Nothing is written to your car."
            : "წერს მხოლოდ-წაკითხვის მოთხოვნებსა და პასუხებს, რომლებსაც აპი მანქანასთან ცვლის სკანირებისას, და ინახავს ფაილად, რომლის გაზიარებაც შეგიძლია. აპი არაფერს ტვირთავს ინტერნეტში. VIN და სერიული ნომრები არ ინახება, მაგრამ მოდულის პასუხი შეიძლება შეიცავდეს იმ წამს დაბრუნებულ მნიშვნელობას. ჩანაწერი მხოლოდ ნედლი მასალაა: ის არცერთ ფუნქციას არ ხდის მხარდაჭერილს. მანქანაში არაფერი იწერება.";

    public string CaptureButtonText =>
        CaptureRecording
            ? (_localization.IsEnglish ? "Stop recording" : "ჩაწერის გაჩერება")
            : (_localization.IsEnglish ? "Start recording" : "ჩაწერის დაწყება");

    public string CaptureShareText =>
        _localization.IsEnglish ? "Save and share recording" : "ჩანაწერის შენახვა და გაზიარება";

    partial void OnCaptureRecordingChanged(bool value) => OnPropertyChanged(nameof(CaptureButtonText));

    private async Task LoadCaptureTargetsAsync()
    {
        try
        {
            _captureTargets = await _capture.GetTargetsAsync();
            var options = new List<string> { OtherVehicleOption };
            options.AddRange(_captureTargets.Select(t => t.DisplayName));
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CaptureTargetOptions = options.ToArray();
                SelectedCaptureTargetIndex = 0;   // replacing the list leaves the picker on "nothing selected"
            });
        }
        catch (Exception ex)
        {
            CarScanner.Services.CommandErrors.Report(ex, "SettingsViewModel.LoadCaptureTargets");
        }
    }

    [RelayCommand]
    private void ToggleCapture()
    {
        try
        {
            if (_capture.IsRecording)
            {
                _capture.Stop();
                CaptureRecording = false;
                CaptureStatus = _localization.IsEnglish
                    ? $"Stopped. {_capture.ItemCount} different exchanges recorded."
                    : $"გაჩერდა. ჩაწერილია {_capture.ItemCount} განსხვავებული გაცვლა.";
                return;
            }

            var index = SelectedCaptureTargetIndex;
            string? targetId = index > 0 && index - 1 < _captureTargets.Count ? _captureTargets[index - 1].Id : null;
            _capture.Start(targetId);
            CaptureRecording = true;
            CaptureStatus = _localization.IsEnglish
                ? "Recording. Connect to the car, then run a scan or the module scan, and come back here to stop."
                : "იწერება. დაუკავშირდი მანქანას, გაუშვი სკანირება ან მოდულების სკანი და დაბრუნდი აქ გასაჩერებლად.";
        }
        catch (Exception ex)
        {
            CarScanner.Services.CommandErrors.Report(ex, "SettingsViewModel.ToggleCapture");
        }
    }

    [RelayCommand]
    private async Task ShareCapture()
    {
        try
        {
            var path = await _capture.SaveAsync();
            if (path is null)
            {
                CaptureStatus = _localization.IsEnglish
                    ? "Nothing recorded yet. Check that the car is connected and recognised, then run a scan."
                    : "ჯერ არაფერი ჩაწერილა. შეამოწმე, რომ მანქანა დაკავშირებული და ამოცნობილია, და გაუშვი სკანირება.";
                return;
            }

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "GCar360 capture",
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            CarScanner.Services.CommandErrors.Report(ex, "SettingsViewModel.ShareCapture");
        }
    }

    partial void OnDataCollectionEnabledChanged(bool value)
    {
        if (!_initialized || _syncingConsent)
            return;

        _ = ApplyDataCollectionAsync(value);
    }

    private async Task ApplyDataCollectionAsync(bool value)
    {
        try
        {
            if (value)
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                var agreed = page is not null && await page.DisplayAlertAsync(
                    _localization.IsEnglish ? "Share compatibility data?" : "თავსებადობის მონაცემების გაზიარება?",
                    _localization.IsEnglish
                        ? "The app will send which standard OBD2 requests and modules your car answers: no VIN, no name, no account, no location. Standard OBD2 requests send no measured values; individual car modules send their raw read-only reply, which can contain a value reported at that moment. A random device code is kept so that repeated reports are not double-counted, so the data is pseudonymous, not fully anonymous. You can turn this off at any time; queued data is deleted and the server is asked to delete what this device sent."
                        : "აპი გააგზავნის, რომელ სტანდარტულ OBD2 მოთხოვნებსა და მოდულებს პასუხობს თქვენი მანქანა: VIN-ის, სახელის, ანგარიშისა და ლოკაციის გარეშე. სტანდარტულ OBD2 მოთხოვნებზე გაზომილი მნიშვნელობები არ იგზავნება; ცალკეული მოდულებისგან იგზავნება ნედლი მხოლოდ-წაკითხვის პასუხი, რომელიც შეიძლება შეიცავდეს იმ წამს დაბრუნებულ მნიშვნელობას. ინახება შემთხვევითი მოწყობილობის კოდი, რომ განმეორებითი გაგზავნა ორჯერ არ დაითვალოს, ამიტომ მონაცემები ფსევდონიმურია და არა სრულად ანონიმური. ნებისმიერ დროს გამორთავ: რიგი წაიშლება და სერვერს მოეთხოვება ამ მოწყობილობის მონაცემების წაშლა.",
                    _localization.IsEnglish ? "Turn on" : "ჩართვა",
                    _localization.IsEnglish ? "Cancel" : "გაუქმება");

                if (!agreed)
                {
                    _syncingConsent = true;
                    DataCollectionEnabled = false;
                    _syncingConsent = false;
                    return;
                }
            }

            await _dataCollection.SetEnabledAsync(value);
        }
        catch (Exception ex)
        {
            CarScanner.Services.CommandErrors.Report(ex, "SettingsViewModel.ApplyDataCollection");
        }
    }

    partial void OnAutoReconnectChanged(bool value)
    {
        Preferences.Default.Set(
            "auto_reconnect",
            value);
    }

    partial void OnUnitOptionChanged(string value)
    {
        _units.Set(value);
        _ = _settings.SetAsync("units", _units.DisplayName);
    }

    partial void OnPollingOptionChanged(string value)
    {
        Preferences.Default.Set(
            "polling",
            value);
    }

    private void ApplyTheme(bool force = false)
    {
        if (Application.Current == null)
            return;

        var normalized =
            ThemeOption switch
            {
                "Light" => "Light",
                "System" => "System",
                _ => "Dark"
            };

        if (ThemeOption != normalized)
            ThemeOption = normalized;

        Application.Current.UserAppTheme =
            normalized switch
            {
                "Light" => AppTheme.Light,
                "System" => AppTheme.Unspecified,
                _ => AppTheme.Dark
            };

        // LocalizationService.Apply no longer re-applies the theme, so the
        // resource palette is written here (forced after a user change).
        ThemeService.Apply(force);

        MainThread.BeginInvokeOnMainThread(
            async () =>
            {
                await Task.Yield();
                _localization.ApplyCurrentPage();
            });
    }

    [RelayCommand]
    private async Task ClearHistory()
    {
        try
        {
        var yes =
            _localization.IsEnglish
                ? "Yes"
                : "დიახ";

        var no =
            _localization.IsEnglish
                ? "No"
                : "არა";

        var confirmed =
            await Shell.Current.DisplayAlertAsync(
                _localization.IsEnglish
                    ? "Confirmation"
                    : "დადასტურება",
                _localization.IsEnglish
                    ? "History will be deleted permanently."
                    : "ისტორია სრულად წაიშლება.",
                yes,
                no);

        if (!confirmed)
            return;

        await _db.ClearHistoryAsync();

        await Shell.Current.DisplayAlertAsync(
            _localization.IsEnglish
                ? "Done"
                : "შესრულდა",
            _localization.IsEnglish
                ? "History deleted."
                : "ისტორია წაიშალა.",
            "OK");
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "SettingsViewModel.ClearHistory");
        }
    }

    [RelayCommand]
    private Task OpenHistory()
    {
        return Shell.Current.GoToAsync(
            nameof(Views.HistoryPage));
    }

    [RelayCommand]
    private Task OpenPro()
    {
        return Shell.Current.GoToAsync(
            nameof(Views.ProPage));
    }

    [RelayCommand]
    private Task OpenContact()
    {
        return Shell.Current.GoToAsync(
            nameof(Views.ContactPage));
    }

    [RelayCommand]
    private async Task PrivacyPolicy()
    {
        try
        {
            await StoreLinks.OpenAsync(StoreLinks.PrivacyPolicyUrl);
        }
        catch
        {
            await Shell.Current.DisplayAlertAsync(
                _localization.IsEnglish
                    ? "Error"
                    : "შეცდომა",
                _localization.IsEnglish
                    ? "Privacy Policy could not be opened."
                    : "კონფიდენციალურობის პოლიტიკის გახსნა ვერ მოხერხდა.",
                "OK");
        }
    }

    [RelayCommand]
    private Task TermsOfUse() => StoreLinks.OpenAsync(StoreLinks.TermsOfUseUrl);

    [RelayCommand]
    private Task ManageSubscription() => StoreLinks.OpenAsync(StoreLinks.ManageSubscriptionUrl);

    [RelayCommand]
    private async Task DeleteVehicleData()
    {
        try
        {
        var yes =
            _localization.IsEnglish
                ? "Delete"
                : "წაშლა";

        var no =
            _localization.IsEnglish
                ? "Cancel"
                : "გაუქმება";

        var title =
            _localization.IsEnglish
                ? "Privacy"
                : "კონფიდენციალურობა";

        var message =
            _localization.IsEnglish
                ? "All local vehicle scans, history and mileage records will be deleted."
                : "ადგილობრივი მანქანის მონაცემები, სკანები, ისტორია და გარბენის ჩანაწერები წაიშლება.";

        var confirmed =
            await Shell.Current.DisplayAlertAsync(
                title,
                message,
                yes,
                no);

        if (!confirmed)
            return;

        await _db.DeleteAllVehicleDataAsync();

        await Shell.Current.DisplayAlertAsync(
            _localization.IsEnglish
                ? "Done"
                : "შესრულდა",
            _localization.IsEnglish
                ? "Local vehicle history was deleted."
                : "ადგილობრივი მანქანის მონაცემები წაიშალა.",
            "OK");
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "SettingsViewModel.DeleteVehicleData");
        }
    }

    [RelayCommand]
    private async Task ExportMyData()
    {
        try
        {
            var json = await _db.ExportDataJsonAsync();

            var path =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    $"gcs-data-export-{DateTime.Now:yyyyMMdd-HHmmss}.json");

            await File.WriteAllTextAsync(
                path,
                json);

            await Share.Default.RequestAsync(
                new ShareFileRequest
                {
                    Title = LocalizationService.T("Georgian Car Scanner data"),
                    File = new ShareFile(path)
                });
        }
        catch
        {
            await Shell.Current.DisplayAlertAsync(
                _localization.IsEnglish
                    ? "Export error"
                    : "ექსპორტის შეცდომა",
                _localization.IsEnglish
                    ? "The data export could not be completed."
                    : "მონაცემების ექსპორტი ვერ შესრულდა.",
                "OK");
        }
    }

    [RelayCommand]
    private async Task About()
    {
        try
        {
        await Shell.Current.DisplayAlertAsync(
            "Georgian Car Scanner",
            LocalizationService.T("Safe & Reliable OBD-II Diagnostics"),
            "OK");
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "SettingsViewModel.About");
        }
    }
}
