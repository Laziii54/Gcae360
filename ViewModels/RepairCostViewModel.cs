using System.Collections.ObjectModel;
using CarScanner.Models;
using CarScanner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

public partial class RepairCostViewModel : BaseViewModel
{
    private readonly RepairCostSession _session;
    private readonly RepairCostService _costs;

    public ObservableCollection<RepairCostEstimate> Estimates { get; } = new();

    [ObservableProperty] private string vehicleTitle = "ავტომობილი უცნობია";
    [ObservableProperty] private string sourceMessage = string.Empty;
    [ObservableProperty] private string emptyMessage = string.Empty;
    public bool HasEstimates => Estimates.Count > 0;
    public bool HasNoEstimates => !HasEstimates && !IsBusy;

    public RepairCostViewModel(RepairCostSession session, RepairCostService costs)
    {
        _session = session;
        _costs = costs;
    }

    /// <summary>The cost estimate becomes the "repair" part of the combined report.</summary>
    private void AddCostsToReport()
    {
        var english = CarScanner.Services.Localization.LanguageCatalog.UsesEnglishBase;
        var min = Estimates.Sum(item => item.PartMinGel + item.LaborMinGel);
        var max = Estimates.Sum(item => item.PartMaxGel + item.LaborMaxGel);
        var section = new CarScanner.Services.Reports.ReportSectionItem
        {
            Kind = "repair",
            Title = english ? "Repair cost" : "შეკეთების ფასი",
            Status = Estimates.Any(item => item.Urgency == RepairUrgency.StopNow)
                ? CarScanner.Services.Reports.ReportStatus.Bad
                : CarScanner.Services.Reports.ReportStatus.Warning,
            Summary = $"≈ {min:N0}–{max:N0} ₾".Replace(',', ' ')
        };

        foreach (var item in Estimates)
            section.Lines.Add(new() { Label = $"{item.Code} {item.ProblemName}".Trim(), Value = $"{item.TotalPriceText} · {item.UrgencyText}" });

        CarScanner.Services.Reports.VehicleReportService.Instance?.AddOrReplace(section);
    }

    [RelayCommand]
    private async Task Load()
    {
        try
        {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            Estimates.Clear();
            EmptyMessage = string.Empty;
            var vehicle = _session.Vehicle;
            if (vehicle is null || _session.Codes.Count == 0)
            {
                EmptyMessage = "ჯერ გაუშვი DTC სკანირება — ფასები მხოლოდ რეალურად ნაპოვნ კოდებზე გამოჩნდება.";
                return;
            }

            VehicleTitle = string.Join(" ", new[]
            {
                vehicle.Year,
                vehicle.Manufacturer,
                vehicle.Model
            }.Where(value => !string.IsNullOrWhiteSpace(value) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase)));

            var result = await _costs.EstimateAsync(vehicle, _session.Codes);
            foreach (var item in result.Estimates)
                Estimates.Add(item);
            SourceMessage = result.SourceMessage;
            if (Estimates.Count == 0)
                EmptyMessage = "ამ კოდებისთვის ფასის დიაპაზონი ვერ მომზადდა.";
            else
                AddCostsToReport();
        }
        catch (Exception ex)
        {
            EmptyMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "ფასების შეფასება ვერ მომზადდა. სცადე ხელახლა."
                : ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasEstimates));
            OnPropertyChanged(nameof(HasNoEstimates));
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "RepairCostViewModel.Load");
        }
    }
}
