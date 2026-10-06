using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CarScanner.VinCheck.Models;
using CarScanner.VinCheck.Services;
using CarScanner.VinCheck.Views;

namespace CarScanner.VinCheck.ViewModels;

public partial class VinHistoryViewModel(IVinHistoryDatabaseService database) : ObservableObject
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ObservableCollection<VinSearchHistory> History { get; } = [];
    public ObservableCollection<SavedVehicle> Favorites { get; } = [];

    public string Title => VinText.T("History");
    public string FavoritesText => VinText.T("Favorites");
    public string ClearText => VinText.T("ClearHistory");
    public string OpenText => VinText.T("Open");

    public async Task LoadAsync()
    {
        History.Clear();
        foreach (var x in await database.GetHistoryAsync())
            History.Add(x);

        Favorites.Clear();
        foreach (var x in await database.GetFavoritesAsync())
            Favorites.Add(x);
    }

    [RelayCommand]
    private Task OpenHistoryAsync(VinSearchHistory item) => OpenAsync(item.ReportJson);

    [RelayCommand]
    private Task OpenFavoriteAsync(SavedVehicle item) => OpenAsync(item.ReportJson);

    private static async Task OpenAsync(string json)
    {
        VinReport? report = null;
        try { report = JsonSerializer.Deserialize<VinReport>(json, Json); } catch { }
        if (report is not null)
            await Shell.Current.GoToAsync(nameof(VinReportPage), new ShellNavigationQueryParameters { { "Report", report } });
    }

    [RelayCommand]
    private async Task DeleteAsync(VinSearchHistory item)
    {
        try
        {
        await database.DeleteHistoryAsync(item.Id);
        History.Remove(item);
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinHistoryViewModel.DeleteAsync");
        }
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        try
        {
        if (await Shell.Current.DisplayAlertAsync(Title, ClearText + "?", VinText.T("Yes"), VinText.T("No")))
        {
            await database.ClearHistoryAsync();
            History.Clear();
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VinHistoryViewModel.ClearAsync");
        }
    }
}
