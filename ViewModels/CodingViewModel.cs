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

/// <summary>
/// Coding page: read-only full backups, compare two backups byte by byte,
/// verified settings for the connected car, and the change history with undo.
/// </summary>
public partial class CodingViewModel : BaseViewModel
{
    private readonly CodingService _coding;
    private readonly ProService _pro;
    private readonly LocalizationService _localization;
    private CancellationTokenSource? _cts;

    public ObservableCollection<CodingBackupRow> Backups { get; } = new();
    public ObservableCollection<CodingOptionRow> Options { get; } = new();
    public ObservableCollection<CodingDiffRow> Differences { get; } = new();
    public ObservableCollection<CodingHistoryRow> HistoryRows { get; } = new();

    [ObservableProperty] private string vehicleText = string.Empty;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string progressText = string.Empty;
    [ObservableProperty] private bool hasBackups;
    [ObservableProperty] private bool hasOptions;
    [ObservableProperty] private bool hasNoOptions = true;
    [ObservableProperty] private bool hasHistory;
    [ObservableProperty] private bool hasDifferences;
    [ObservableProperty] private string compareText = string.Empty;
    [ObservableProperty] private bool hasCompareText;

    public CodingViewModel(CodingService coding, ProService pro, LocalizationService localization)
    {
        _coding = coding;
        _pro = pro;
        _localization = localization;
        Title = L("კოდირება", "Coding");
        _localization.LanguageChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            Title = L("კოდირება", "Coding");
            _ = RefreshAsync();
        });
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    /// <summary>"Change" — marked PRO until PRO is active (backups stay free).</summary>
    public string ChangeButtonText => L("შეცვლა", "Change") + ProGate.Suffix;

    public async Task OnAppearingAsync()
    {
        try
        {
            OnPropertyChanged(nameof(ChangeButtonText));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            CommandErrors.Report(ex, "Coding.OnAppearing");
        }
    }

    public void OnDisappearing() => _cts?.Cancel();

    private async Task RefreshAsync()
    {
        VehicleText = _coding.IsConnected
            ? _coding.VehicleName + (_coding.Vehicle?.VIN is { Length: 17 } vin ? $" · VIN …{vin[^6..]}" : string.Empty)
            : L("ადაპტერი არ არის დაკავშირებული — სარეზერვო ასლები მაინც ჩანს", "Adapter not connected — saved backups are still shown");

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
    }

    private void Report(double fraction, string text)
    {
        Progress = fraction;
        ProgressText = text;
    }

    [RelayCommand]
    private async Task CreateBackup()
    {
        if (IsBusy) return;
        try
        {
            if (!_coding.IsConnected)
            {
                await Shell.Current.DisplayAlertAsync(L("კოდირება", "Coding"), L("ჯერ OBD ადაპტერს დაუკავშირდით და ჩართეთ ანთება.", "Connect the OBD adapter and switch the ignition on first."), "OK");
                return;
            }

            var note = await Shell.Current.DisplayPromptAsync(
                L("სარეზერვო ასლი", "Backup"),
                L("მოკლე შენიშვნა (მაგ. „ქარხნული“ ან „DRL ჩართვის შემდეგ“)", "Short note (e.g. \"factory\" or \"after DRL on\")"),
                "OK", L("გაუქმება", "Cancel"), maxLength: 60);
            if (note is null) return;

            IsBusy = true;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var progress = new Progress<(double Fraction, string Text)>(p => Report(p.Fraction, p.Text));
            var backup = await _coding.CreateBackupAsync(note.Trim(), progress, _cts.Token);
            await RefreshAsync();
            Report(1, L($"შენახულია: {backup.Modules.Count} მოდული, {backup.ReadCount} მნიშვნელობა. მანქანაზე არაფერი შეცვლილა.",
                $"Saved: {backup.Modules.Count} modules, {backup.ReadCount} values. Nothing was changed on the car."));
        }
        catch (OperationCanceledException)
        {
            Report(0, L("გაუქმდა", "Cancelled"));
        }
        catch (InvalidOperationException ex)
        {
            Report(0, ex.Message);
        }
        catch (Exception ex)
        {
            Report(0, L("სარეზერვო ასლი ვერ შეიქმნა.", "Backup failed."));
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
            var path = Path.Combine(FileSystem.CacheDirectory, $"gcar360_coding_{row.Backup.CreatedAt:yyyyMMdd_HHmm}.txt");
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
            var ok = await Shell.Current.DisplayAlertAsync(L("წაშლა", "Delete"), L("წავშალო ეს სარეზერვო ასლი?", "Delete this backup?"), L("წაშლა", "Delete"), L("გაუქმება", "Cancel"));
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
            var picked = await Shell.Current.DisplayActionSheetAsync(row.Title, L("გაუქმება", "Cancel"), null, choices);
            var target = row.Option.Values.FirstOrDefault(v => L(v.Ka, v.En) == picked);
            if (target is null) return;

            IsBusy = true;
            var checks = await _coding.CheckConditionsAsync(row.Option, CancellationToken.None);
            IsBusy = false;
            var list = string.Join("\n", checks.Select(c => (c.Ok ? "✓ " : "✗ ") + c.Text));
            if (checks.Any(c => !c.Ok))
            {
                await Shell.Current.DisplayAlertAsync(L("ჯერ არ შეიძლება", "Not yet"), list, "OK");
                return;
            }

            var confirm = await Shell.Current.DisplayAlertAsync(
                L("დაადასტურეთ ცვლილება", "Confirm the change"),
                L($"{row.Title} → {target.Ka}\n\n{list}\n\nჯერ შეიქმნება სრული სარეზერვო ასლი. ჩაწერის შემდეგ მნიშვნელობა შემოწმდება და შეუსაბამობისას თავდაპირველი ავტომატურად დაბრუნდება. პროცესის დროს არ გამორთოთ ანთება და ადაპტერი.",
                  $"{row.Title} → {target.En}\n\n{list}\n\nA full backup is made first. After writing, the value is read back; on any mismatch the original is restored automatically. Do not switch off the ignition or unplug the adapter."),
                L("შეცვლა", "Change"), L("გაუქმება", "Cancel"));
            if (!confirm) return;

            IsBusy = true;
            var progress = new Progress<string>(text => Report(0.5, text));
            var result = await _coding.WriteOptionAsync(row.Option, target, progress, CancellationToken.None);
            Report(result.Success ? 1 : 0, result.Message);
            await Shell.Current.DisplayAlertAsync(result.Success ? L("მზადაა", "Done") : L("არ შეიცვალა", "Not changed"), result.Message, "OK");
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
            var ok = await Shell.Current.DisplayAlertAsync(L("აღდგენა", "Restore"),
                L($"დავაბრუნო თავდაპირველი მნიშვნელობა?\n{row.Title}", $"Put the original value back?\n{row.Title}"),
                L("აღდგენა", "Restore"), L("გაუქმება", "Cancel"));
            if (!ok) return;

            IsBusy = true;
            var progress = new Progress<string>(text => Report(0.5, text));
            var result = await _coding.RestoreAsync(row.Change, progress, CancellationToken.None);
            Report(result.Success ? 1 : 0, result.Message);
            await Shell.Current.DisplayAlertAsync(result.Success ? L("მზადაა", "Done") : L("ვერ აღდგა", "Not restored"), result.Message, "OK");
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
