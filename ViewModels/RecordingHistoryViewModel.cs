using System.Collections.ObjectModel;
using CarScanner.Models;
using CarScanner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

public partial class RecordingHistoryViewModel : BaseViewModel
{
    private readonly RecordingSessionService _recordings;

    public ObservableCollection<RecordingSession> Sessions { get; } = new();

    [ObservableProperty] private string emptyMessage = "ჩაწერილი Live Data სესიები ჯერ არ არის.";

    public RecordingHistoryViewModel(RecordingSessionService recordings)
    {
        _recordings = recordings;
        Title = "Data Recording";
    }

    [RelayCommand]
    public async Task Load()
    {
        try
        {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            Sessions.Clear();
            foreach (var session in await _recordings.GetSessionsAsync())
                Sessions.Add(session);
            EmptyMessage = Sessions.Count == 0
                ? "ჩაწერილი Live Data სესიები ჯერ არ არის. Record-ით დაიწყე რეალური OBD მონაცემების ჩაწერა."
                : string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "RecordingHistoryViewModel.Load");
        }
    }

    [RelayCommand]
    private async Task ShareCsv(RecordingSession? session)
    {
        try
        {
        if (session is null)
            return;
        var path = await _recordings.CreateCsvExportAsync(session);
        if (path is null)
        {
            StatusMessage = "ამ სესიის ფაილი ვერ მოიძებნა.";
            return;
        }
        await Share.Default.RequestAsync(new ShareFileRequest { Title = "Live Data CSV", File = new ShareFile(path) });
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "RecordingHistoryViewModel.ShareCsv");
        }
    }

    [RelayCommand]
    private async Task ShareJson(RecordingSession? session)
    {
        try
        {
        if (session is null)
            return;
        var path = await _recordings.CreateJsonExportAsync(session);
        if (path is null)
        {
            StatusMessage = "ამ სესიის ფაილი ვერ მოიძებნა.";
            return;
        }
        await Share.Default.RequestAsync(new ShareFileRequest { Title = "Live Data JSON", File = new ShareFile(path) });
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "RecordingHistoryViewModel.ShareJson");
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                // Android's picker filters are advisory. The imported content
                // is validated as a Live Data JSON document before saving.
                PickerTitle = "აირჩიე Live Data JSON"
            });
            if (file is null)
                return;

            var imported = await _recordings.ImportAsync(file);
            StatusMessage = imported is null
                ? "ფაილში წასაკითხი Live Data ჩანაწერი ვერ მოიძებნა."
                : $"სესია დამატებულია: {imported.SampleCount} მონაცემი.";
            await Load();
        }
        catch (Exception ex)
        {
            StatusMessage = $"იმპორტი ვერ შესრულდა: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Delete(RecordingSession? session)
    {
        try
        {
        if (session is null)
            return;

        var confirmed = await Shell.Current.DisplayAlertAsync(
            CarScanner.Services.Localization.LocalizationService.T("სესიის წაშლა"),
            CarScanner.Services.Localization.LocalizationService.T($"წაიშალოს {session.DateText} ჩანაწერი? ეს მოქმედება ვერ დაბრუნდება."),
            CarScanner.Services.Localization.LocalizationService.T("წაშლა"),
            CarScanner.Services.Localization.LocalizationService.T("გაუქმება"));
        if (!confirmed)
            return;

        await _recordings.DeleteAsync(session);
        Sessions.Remove(session);
        EmptyMessage = Sessions.Count == 0 ? "ჩაწერილი Live Data სესიები ჯერ არ არის." : string.Empty;
        StatusMessage = "ჩანაწერი წაიშალა.";
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "RecordingHistoryViewModel.Delete");
        }
    }
}
