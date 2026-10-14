using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CarScanner.Models;
using CarScanner.Services;
using System.Collections.ObjectModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace CarScanner.ViewModels;

public partial class HistoryViewModel : BaseViewModel
{
    private readonly DatabaseService _db;

    public ObservableCollection<ScanResult> History { get; } = new();
    public ObservableCollection<ScanSession> Sessions { get; } = new();

    public HistoryViewModel(DatabaseService db)
    {
        _db = db;
        Title = "ისტორია";
    }

    [RelayCommand]
    private async Task Load()
    {
        try
        {
        if (IsBusy)
            return;

        IsBusy = true;

        try
        {
            var historyTask = _db.GetHistoryAsync();
            var sessionsTask = _db.GetScanSessionsAsync();
            await Task.WhenAll(historyTask, sessionsTask);

            History.Clear();
            foreach (var item in await historyTask)
                History.Add(item);

            Sessions.Clear();
            foreach (var session in await sessionsTask)
                Sessions.Add(session);
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HistoryViewModel.Load");
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        try
        {
            var historyTask = _db.GetHistoryAsync();
            var sessionsTask = _db.GetScanSessionsAsync();
            await Task.WhenAll(historyTask, sessionsTask);
            var items = await historyTask;
            var sessions = await sessionsTask;

            if (items.Count == 0 && sessions.Count == 0)
            {
                await Shell.Current.DisplayAlertLocalizedAsync(
                    CarScanner.Services.Localization.LocalizationService.T("ექსპორტი"),
                    CarScanner.Services.Localization.LocalizationService.T("ისტორია ცარიელია."),
                    CarScanner.Services.Localization.LocalizationService.T("კარგი"));

                return;
            }

            var sb = new System.Text.StringBuilder();

            sb.AppendLine("Type,Date,Vehicle,Protocol,Stored,Pending,Permanent,Codes,Notes");

            foreach (var session in sessions)
            {
                sb.AppendLine(
                    $"Detailed,{session.StartedAt:yyyy-MM-dd HH:mm:ss}," +
                    $"{Csv(session.VehicleSummary)},{Csv(session.Protocol)}," +
                    $"{session.StoredDtcCount},{session.PendingDtcCount}," +
                    $"{session.PermanentDtcCount},,");
            }

            foreach (var item in items)
            {
                sb.AppendLine(
                    $"Legacy,{item.ScanDate:yyyy-MM-dd HH:mm:ss}," +
                    $"{Csv(item.CarInfo)},,,,,{Csv(item.FoundCodes)}," +
                    $"{Csv(item.Notes)}");
            }

            var path = Path.Combine(
                FileSystem.CacheDirectory,
                $"car-scanner-history-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            await File.WriteAllTextAsync(
                path,
                sb.ToString(),
                System.Text.Encoding.UTF8);

            await Share.Default.RequestAsync(
                new ShareFileRequest
                {
                    Title = "OBD360 History",
                    File = new ShareFile(path)
                });
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertLocalizedAsync(
                CarScanner.Services.Localization.LocalizationService.T("ექსპორტის შეცდომა"),
                CarScanner.Services.UserError.Describe(ex),
                CarScanner.Services.Localization.LocalizationService.T("კარგი"));
        }
    }

    private static string Csv(string? value) =>
        "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

    [RelayCommand]
    private async Task Clear()
    {
        try
        {
        var confirm = await Shell.Current.DisplayAlertLocalizedAsync(
            CarScanner.Services.Localization.LocalizationService.T("დადასტურება"),
            CarScanner.Services.Localization.LocalizationService.T("წაიშლება ყველა შენახული სკანი, DTC, სენსორის მონაცემი, readiness, ECU ინფორმაცია და freeze frame. გსურთ გაგრძელება?"),
            CarScanner.Services.Localization.LocalizationService.T("დიახ"),
            CarScanner.Services.Localization.LocalizationService.T("არა"));

        if (!confirm)
            return;

        await _db.ClearHistoryAsync();

        History.Clear();
        Sessions.Clear();
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "HistoryViewModel.Clear");
        }
    }
}
