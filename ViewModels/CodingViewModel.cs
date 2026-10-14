using System.Collections.ObjectModel;
using CarScanner.Services;
using CarScanner.Services.Coding;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

public sealed partial class CodingBackupRow : ObservableObject
{
    public CodingBackup Backup { get; init; } = new();
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    [ObservableProperty] private bool isSelected;
}

public sealed partial class CodingOptionRow : ObservableObject
{
    public CodingOption Option { get; init; } = new();
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    [ObservableProperty] private string currentText = "—";
}

public sealed record CodingDiffRow(string Module, string Location, string Change);

public sealed record CodingHistoryRow(CodingChange Change, string Title, string Detail, bool CanRestore);

/// <summary>One line of the readiness list: a mark, the text, and the colour of the state.</summary>
/// <summary><c>Mark</c> is an icon kind from Controls/IconCatalog.</summary>
public sealed record CodingCheckRow(string Mark, string Text, Color MarkColor);

/// <summary>One feature of the page with the state it is in for this car right now, and why.</summary>
public sealed record CodingFeatureRow(string Title, string StateText, string Reason, Color StateColor, Color StateBackground);

/// <summary>
/// Coding page: read-only full backups, compare two backups byte by byte, verified settings for the connected car,
/// and the change history with undo. The page says what is available for THIS car and why, and never presents a
/// setting as supported unless the verified catalog lists it for this exact vehicle and module.
/// </summary>
public partial class CodingViewModel : BaseViewModel
{
    private readonly CodingService _coding;
    private readonly ProService _pro;
    private readonly LocalizationService _localization;
    private CancellationTokenSource? _cts;
    private CodingReadinessInput? _readiness;
    private int _verifiedInCatalog;
    private bool _lastFailureWasBackup;

    public ObservableCollection<CodingBackupRow> Backups { get; } = new();
    public ObservableCollection<CodingOptionRow> Options { get; } = new();
    public ObservableCollection<CodingDiffRow> Differences { get; } = new();
    public ObservableCollection<CodingHistoryRow> HistoryRows { get; } = new();
    public ObservableCollection<CodingCheckRow> ReadinessRows { get; } = new();
    public ObservableCollection<CodingFeatureRow> FeatureRows { get; } = new();

    [ObservableProperty] private string vehicleText = string.Empty;
    [ObservableProperty] private string vinText = string.Empty;
    [ObservableProperty] private string protocolText = string.Empty;
    [ObservableProperty] private string connectionChipText = string.Empty;
    [ObservableProperty] private Color connectionColor = Colors.Gray;
    [ObservableProperty] private Color connectionBackground = Colors.LightGray;
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string progressText = string.Empty;
    [ObservableProperty] private bool hasBackups;
    [ObservableProperty] private bool hasOptions;
    [ObservableProperty] private bool hasNoOptions = true;
    [ObservableProperty] private string noOptionsText = string.Empty;
    [ObservableProperty] private bool hasHistory;
    [ObservableProperty] private bool hasDifferences;
    [ObservableProperty] private string compareText = string.Empty;
    [ObservableProperty] private bool hasCompareText;
    [ObservableProperty] private string errorText = string.Empty;
    [ObservableProperty] private bool hasError;
    [ObservableProperty] private bool isGmCar;
    [ObservableProperty] private string lastBackupSummary = string.Empty;
    [ObservableProperty] private bool hasLastBackupSummary;
    [ObservableProperty] private bool showReadiness;
    [ObservableProperty] private bool showSafetyNotice;
    [ObservableProperty] private bool showNoOptionsHelp;

    [RelayCommand]
    private void ToggleReadiness() => ShowReadiness = !ShowReadiness;

    [RelayCommand]
    private void ToggleSafetyNotice() => ShowSafetyNotice = !ShowSafetyNotice;

    [RelayCommand]
    private void ToggleNoOptionsHelp() => ShowNoOptionsHelp = !ShowNoOptionsHelp;

    public CodingViewModel(CodingService coding, ProService pro, LocalizationService localization)
    {
        _coding = coding;
        _pro = pro;
        _localization = localization;
        Title = L("კოდირება", "Coding");
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsBusy))
                OnPropertyChanged(nameof(IsNotBusy));
        };
        _localization.LanguageChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            Title = L("კოდირება", "Coding");
            _ = RefreshAsync();
        });
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    public bool IsNotBusy => !IsBusy;

    // Section captions are bound (not static XAML text) so they follow the language like everything else on the page.
    public string ReadinessCaption => L("მზადყოფნა", "Readiness");
    public string FeaturesCaption => L("რა მუშაობს ამ მანქანაზე", "What works for this car");
    public string RecheckText => L("ხელახლა შემოწმება", "Check again");
    public string ConnectText => L("ადაპტერის დაკავშირება", "Connect the adapter");
    public string RetryText => L("ხელახლა ცდა", "Try again");
    public string LastBackupCaption => L("ბოლო სარეზერვო ასლი", "Last backup");
    public string ExtendedReadText => CodingTexts.ExtendedReadTitle(_localization.IsEnglish);
    public string ExtendedReadHelp => CodingTexts.ExtendedReadHelp(_localization.IsEnglish);
    public string ShareNoticeText => L("გაზიარებულ ფაილში VIN ჩანს.", "The shared file contains the VIN.");

    /// <summary>"Change" — marked PRO until PRO is active (backups stay free).</summary>
    public string ChangeButtonText => L("შეცვლა", "Change") + ProGate.Suffix;

    public async Task OnAppearingAsync()
    {
        try
        {
            OnPropertyChanged(nameof(ChangeButtonText));
            await RefreshAsync();
            await RecheckAsync(announce: false);
        }
        catch (Exception ex)
        {
            CommandErrors.Report(ex, "Coding.OnAppearing");
        }
    }

    public void OnDisappearing() => _cts?.Cancel();

    private void SetConnectionChip()
    {
        IsConnected = _coding.IsConnected;
        if (!IsConnected)
        {
            ConnectionChipText = L("არ არის დაკავშირებული", "Not connected");
            ConnectionColor = ThemeService.Soft("#5D636B");
            ConnectionBackground = ThemeService.Soft("#EEF0F2");
        }
        else if (!_coding.IsCanConnection)
        {
            ConnectionChipText = L("დაკავშირებულია · არა CAN", "Connected · not CAN");
            ConnectionColor = ThemeService.Soft("#B3361F");
            ConnectionBackground = ThemeService.Soft("#FCEDEA");
        }
        else
        {
            ConnectionChipText = L("დაკავშირებულია", "Connected");
            ConnectionColor = ThemeService.Soft("#1E8A4C");
            ConnectionBackground = ThemeService.Soft("#E6F4EC");
        }
    }

    private async Task RefreshAsync()
    {
        OnPropertyChanged(nameof(ReadinessCaption));
        OnPropertyChanged(nameof(FeaturesCaption));
        OnPropertyChanged(nameof(RecheckText));
        OnPropertyChanged(nameof(ConnectText));
        OnPropertyChanged(nameof(RetryText));
        OnPropertyChanged(nameof(LastBackupCaption));
        OnPropertyChanged(nameof(ShareNoticeText));
        OnPropertyChanged(nameof(ExtendedReadText));
        OnPropertyChanged(nameof(ExtendedReadHelp));
        OnPropertyChanged(nameof(ChangeButtonText));

        IsGmCar = _coding.IsGmVehicle;
        SetConnectionChip();
        VehicleText = _coding.IsConnected
            ? _coding.VehicleName
            : L("მანქანა არ არის დაკავშირებული", "No vehicle connected");
        VinText = _coding.Vehicle?.VIN is { Length: 17 } vin
            ? $"VIN …{vin[^6..]}"
            : (_coding.IsConnected ? L("VIN უცნობია", "VIN unknown") : L("შენახული ასლები მაინც ჩანს", "Saved backups are still shown"));
        ProtocolText = _coding.ProtocolText;

        Backups.Clear();
        foreach (var backup in _coding.ListBackups())
        {
            Backups.Add(new CodingBackupRow
            {
                Backup = backup,
                Title = backup.CreatedAt.ToString("dd.MM.yyyy HH:mm") + (string.IsNullOrWhiteSpace(backup.Note) ? string.Empty : " · " + backup.Note),
                Detail = L($"{backup.Modules.Count} მოდული · {backup.ReadCount} მნიშვნელობა", $"{backup.Modules.Count} modules · {backup.ReadCount} values")
            });
        }
        HasBackups = Backups.Count > 0;

        Options.Clear();
        foreach (var option in await _coding.OptionsForCurrentCarAsync())
        {
            Options.Add(new CodingOptionRow
            {
                Option = option,
                Title = L(option.TitleKa, option.TitleEn),
                Description = L(option.DescriptionKa, option.DescriptionEn)
            });
        }
        HasOptions = Options.Count > 0;
        HasNoOptions = !HasOptions;

        HistoryRows.Clear();
        foreach (var change in _coding.History())
        {
            HistoryRows.Add(new CodingHistoryRow(
                change,
                change.Title,
                change.At.ToString("dd.MM.yyyy HH:mm") + $" · {change.Did}: {change.BeforeHex} → {change.AfterHex}" +
                    (change.Restored ? L(" · აღდგენილია", " · restored") : string.Empty),
                !change.Restored));
        }
        HasHistory = HistoryRows.Count > 0;

        _verifiedInCatalog = await _coding.VerifiedOptionCountInCatalogAsync();
        BuildFeatureRows();
        BuildReadinessRows();
        NoOptionsText = CurrentChangeReason();
    }

    private CodingInputs CurrentInputs() => new(
        _coding.IsConnected,
        _coding.IsCanConnection,
        _coding.Vehicle?.VIN is { Length: 17 },
        Options.Count,
        _verifiedInCatalog,
        Backups.Count,
        HistoryRows.Count(row => row.CanRestore),
        _pro.HasObd,
        _coding.IsGmVehicle);

    private string CurrentChangeReason()
    {
        var input = CurrentInputs();
        var status = CodingCapabilities.Evaluate(input).Single(item => item.Feature == CodingFeature.ChangeSettings);
        return CodingTexts.Reason(status, input, _localization.IsEnglish);
    }

    private Color StateColor(CodingFeatureState state) => ThemeService.Soft(state switch
    {
        CodingFeatureState.Available => "#1E8A4C",
        CodingFeatureState.NotCan => "#B3361F",
        CodingFeatureState.NotVerifiedForThisVehicle or CodingFeatureState.ProRequired or CodingFeatureState.NeedsConnection => "#B06A00",
        _ => "#5D636B"
    });

    private Color StateBackground(CodingFeatureState state) => ThemeService.Soft(state switch
    {
        CodingFeatureState.Available => "#E6F4EC",
        CodingFeatureState.NotCan => "#FCEDEA",
        CodingFeatureState.NotVerifiedForThisVehicle or CodingFeatureState.ProRequired or CodingFeatureState.NeedsConnection => "#FBF0D9",
        _ => "#EEF0F2"
    });

    private void BuildFeatureRows()
    {
        var input = CurrentInputs();
        var en = _localization.IsEnglish;
        FeatureRows.Clear();
        foreach (var status in CodingCapabilities.Evaluate(input))
        {
            FeatureRows.Add(new CodingFeatureRow(
                CodingTexts.Title(status.Feature, en),
                CodingTexts.StateText(status.State, en),
                CodingTexts.Reason(status, input, en),
                StateColor(status.State),
                StateBackground(status.State)));
        }
    }

    private void BuildReadinessRows()
    {
        ReadinessRows.Clear();
        var input = _readiness ?? new CodingReadinessInput(_coding.IsConnected, _coding.IsCanConnection, _coding.Vehicle?.VIN, null, null, false);
        var en = _localization.IsEnglish;
        foreach (var check in CodingCapabilities.Readiness(input))
        {
            var (mark, hex) = check.State switch
            {
                CodingCheckState.Ok => ("check", "#1E8A4C"),
                CodingCheckState.Warning => ("warning", "#B06A00"),
                CodingCheckState.Blocked => ("close", "#B3361F"),
                _ => ("circle", "#5D636B")
            };
            ReadinessRows.Add(new CodingCheckRow(mark, check.Text(en), ThemeService.Soft(hex)));
        }
    }

    /// <summary>Re-reads speed and battery voltage (read-only) and rebuilds the readiness list.</summary>
    private async Task RecheckAsync(bool announce)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            if (announce)
                Report(0, L("ვამოწმებ…", "Checking…"));
            _cts?.Cancel();
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _readiness = await _coding.ReadReadinessAsync(_cts.Token);
            SetConnectionChip();
            BuildReadinessRows();
            if (announce)
                Report(0, string.Empty);
        }
        catch (OperationCanceledException)
        {
            Report(0, string.Empty);
        }
        catch (Exception ex)
        {
            CommandErrors.Report(ex, "Coding.Recheck");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task Recheck() => RecheckAsync(announce: true);

    [RelayCommand]
    private async Task Connect()
    {
        try
        {
            await CarScanner.Services.SafeNavigation.GoToAsync(nameof(Views.BluetoothPage));
        }
        catch (Exception ex)
        {
            CommandErrors.Report(ex, "Coding.Connect");
        }
    }

    [RelayCommand]
    private async Task Retry()
    {
        HasError = false;
        ErrorText = string.Empty;
        if (_lastFailureWasBackup)
            await CreateBackup();
        else
            await RefreshAsync();
    }

    private void Report(double fraction, string text)
    {
        Progress = fraction;
        ProgressText = text;
    }

    private void Fail(string message, bool backup)
    {
        _lastFailureWasBackup = backup;
        ErrorText = message;
        HasError = true;
        Report(0, string.Empty);
    }

    [RelayCommand]
    private Task CreateBackup() => BackupAsync(extended: false);

    /// <summary>GM only: the normal backup plus a read-only sweep of the GMLAN $1A identifiers.</summary>
    [RelayCommand]
    private Task CreateExtendedBackup() => BackupAsync(extended: true);

    private async Task BackupAsync(bool extended)
    {
        if (IsBusy) return;
        try
        {
            HasError = false;
            if (!_coding.IsConnected)
            {
                await Shell.Current.DisplayAlertLocalizedAsync(L("კოდირება", "Coding"), L("ჯერ OBD ადაპტერს დაუკავშირდით და ჩართეთ ანთება.", "Connect the OBD adapter and switch the ignition on first."), "OK");
                return;
            }

            var note = await Shell.Current.DisplayPromptLocalizedAsync(
                L("სარეზერვო ასლი", "Backup"),
                L("მოკლე შენიშვნა (მაგ. „ქარხნული“ ან „DRL ჩართვის შემდეგ“)", "Short note (e.g. \"factory\" or \"after DRL on\")"),
                "OK", L("გაუქმება", "Cancel"), maxLength: 60);
            if (note is null) return;

            IsBusy = true;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var progress = new Progress<(double Fraction, string Text)>(p => Report(p.Fraction, p.Text));
            var backup = await _coding.CreateBackupAsync(note.Trim(), progress, _cts.Token, extended);
            await RefreshAsync();
            Report(1, L($"შენახულია: {backup.Modules.Count} მოდული, {backup.ReadCount} მნიშვნელობა. მანქანაზე არაფერი შეცვლილა.",
                $"Saved: {backup.Modules.Count} modules, {backup.ReadCount} values. Nothing was changed on the car."));

            var lines = new List<string>
            {
                L($"{backup.Modules.Count(m => m.Status == CodingBackupStatus.Read)} მოდულიდან წაიკითხა მნიშვნელობები, {backup.Modules.Count(m => m.Status != CodingBackupStatus.Read)} მოდულიდან — არა.",
                  $"Values were read from {backup.Modules.Count(m => m.Status == CodingBackupStatus.Read)} module(s); not from {backup.Modules.Count(m => m.Status != CodingBackupStatus.Read)}.")
            };
            lines.AddRange(backup.Warnings);
            LastBackupSummary = string.Join("\n", lines);
            HasLastBackupSummary = true;
        }
        catch (OperationCanceledException)
        {
            Report(0, L("გაუქმდა", "Cancelled"));
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message, backup: true);
        }
        catch (Exception ex)
        {
            Fail(L("სარეზერვო ასლი ვერ შეიქმნა. შეამოწმეთ ადაპტერი და სცადეთ თავიდან.", "The backup could not be created. Check the adapter and try again."), backup: true);
            CommandErrors.Report(ex, "Coding.CreateBackup");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ShareBackup(CodingBackupRow? row)
    {
        if (row is null) return;
        try
        {
            var path = Path.Combine(FileSystem.CacheDirectory, $"obd360_coding_{row.Backup.CreatedAt:yyyyMMdd_HHmm}.txt");
            await File.WriteAllTextAsync(path, CodingService.BuildText(row.Backup) + Environment.NewLine + Environment.NewLine +
                System.Text.Json.JsonSerializer.Serialize(row.Backup));
            await Share.Default.RequestAsync(new ShareFileRequest { Title = L("კოდირების სარეზერვო ასლი", "Coding backup"), File = new ShareFile(path) });
        }
        catch (Exception ex)
        {
            CommandErrors.Report(ex, "Coding.ShareBackup");
        }
    }

    [RelayCommand]
    private async Task DeleteBackup(CodingBackupRow? row)
    {
        if (row is null) return;
        try
        {
            var ok = await Shell.Current.DisplayAlertLocalizedAsync(L("წაშლა", "Delete"), L("წავშალო ეს სარეზერვო ასლი?", "Delete this backup?"), L("წაშლა", "Delete"), L("გაუქმება", "Cancel"));
            if (!ok) return;
            _coding.DeleteBackup(row.Backup);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            CommandErrors.Report(ex, "Coding.DeleteBackup");
        }
    }

    [RelayCommand]
    private void ToggleCompare(CodingBackupRow? row)
    {
        if (row is null) return;
        row.IsSelected = !row.IsSelected;
        var selected = Backups.Where(b => b.IsSelected).ToList();
        if (selected.Count > 2)
        {
            foreach (var other in selected.Where(b => b != row).Skip(1)) other.IsSelected = false;
            selected = Backups.Where(b => b.IsSelected).ToList();
        }

        Differences.Clear();
        if (selected.Count < 2)
        {
            CompareText = L("შედარებისთვის მონიშნეთ ორი სარეზერვო ასლი.", "Select two backups to compare.");
            HasCompareText = selected.Count == 1;
            HasDifferences = false;
            return;
        }

        var ordered = selected.OrderBy(b => b.Backup.CreatedAt).ToList();
        foreach (var diff in CodingService.Compare(ordered[0].Backup, ordered[1].Backup))
        {
            Differences.Add(new CodingDiffRow(
                diff.Module,
                L($"{diff.Did} · ბაიტი {diff.ByteIndex}", $"{diff.Did} · byte {diff.ByteIndex}"),
                $"{diff.Before} → {diff.After}" + (diff.BitsChanged == "—" ? string.Empty : L($" · ბიტი {diff.BitsChanged}", $" · bit {diff.BitsChanged}"))));
        }
        HasDifferences = Differences.Count > 0;
        CompareText = HasDifferences
            ? L($"{Differences.Count} ცვლილება (ძველი → ახალი)", $"{Differences.Count} changes (older → newer)")
            : L("განსხვავება არ არის — ამ ბლოკებში არაფერი შეცვლილა.", "No difference — nothing changed in these blocks.");
        HasCompareText = true;
    }

    [RelayCommand]
    private async Task ReadOption(CodingOptionRow? row)
    {
        if (row is null || IsBusy) return;
        try
        {
            IsBusy = true;
            var state = await _coding.ReadOptionAsync(row.Option, CancellationToken.None);
            row.CurrentText = state.Current is not null
                ? L("ახლა: ", "Now: ") + L(state.Current.Ka, state.Current.En)
                : state.Error;
        }
        catch (Exception ex)
        {
            row.CurrentText = L("ვერ წავიკითხე", "Could not read");
            CommandErrors.Report(ex, "Coding.ReadOption");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ChangeOption(CodingOptionRow? row)
    {
        if (row is null || IsBusy) return;
        try
        {
            if (!await ProGate.EnsureOrOpenAsync(_pro)) return;

            var choices = row.Option.Values.Select(v => L(v.Ka, v.En)).ToArray();
            var picked = await Shell.Current.DisplayActionSheetLocalizedAsync(row.Title, L("გაუქმება", "Cancel"), null, choices);
            var target = row.Option.Values.FirstOrDefault(v => L(v.Ka, v.En) == picked);
            if (target is null) return;

            IsBusy = true;
            var checks = await _coding.CheckConditionsAsync(row.Option, CancellationToken.None);
            IsBusy = false;
            var list = string.Join("\n", checks.Select(c => (c.Ok ? L("შესრულებულია: ", "Met: ") : L("არ არის შესრულებული: ", "Not met: ")) + c.Text));
            if (checks.Any(c => !c.Ok))
            {
                await Shell.Current.DisplayAlertLocalizedAsync(L("ჯერ არ შეიძლება", "Not yet"), list, "OK");
                return;
            }

            var confirm = await Shell.Current.DisplayAlertLocalizedAsync(
                L("დაადასტურეთ ცვლილება", "Confirm the change"),
                L($"{row.Title} → {target.Ka}\n\n{list}\n\nჯერ შეიქმნება სრული სარეზერვო ასლი. ჩაწერის შემდეგ მნიშვნელობა შემოწმდება და შეუსაბამობისას თავდაპირველი ავტომატურად დაბრუნდება. პროცესის დროს არ გამორთოთ ანთება და ადაპტერი.",
                  $"{row.Title} → {target.En}\n\n{list}\n\nA full backup is made first. After writing, the value is read back; on any mismatch the original is restored automatically. Do not switch off the ignition or unplug the adapter."),
                L("შეცვლა", "Change"), L("გაუქმება", "Cancel"));
            if (!confirm) return;

            IsBusy = true;
            var progress = new Progress<string>(text => Report(0.5, text));
            var result = await _coding.WriteOptionAsync(row.Option, target, progress, CancellationToken.None);
            Report(result.Success ? 1 : 0, result.Message);
            await Shell.Current.DisplayAlertLocalizedAsync(result.Success ? L("მზადაა", "Done") : L("არ შეიცვალა", "Not changed"), result.Message, "OK");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            CommandErrors.Report(ex, "Coding.ChangeOption");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Restore(CodingHistoryRow? row)
    {
        if (row is null || !row.CanRestore || IsBusy) return;
        try
        {
            var ok = await Shell.Current.DisplayAlertLocalizedAsync(L("აღდგენა", "Restore"),
                L($"დავაბრუნო თავდაპირველი მნიშვნელობა?\n{row.Title}", $"Put the original value back?\n{row.Title}"),
                L("აღდგენა", "Restore"), L("გაუქმება", "Cancel"));
            if (!ok) return;

            IsBusy = true;
            var progress = new Progress<string>(text => Report(0.5, text));
            var result = await _coding.RestoreAsync(row.Change, progress, CancellationToken.None);
            Report(result.Success ? 1 : 0, result.Message);
            await Shell.Current.DisplayAlertLocalizedAsync(result.Success ? L("მზადაა", "Done") : L("ვერ აღდგა", "Not restored"), result.Message, "OK");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            CommandErrors.Report(ex, "Coding.Restore");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
