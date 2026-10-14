using CarScanner.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CarScanner.Services;

namespace CarScanner.ViewModels;

public partial class VehicleTypeSelectionViewModel : BaseViewModel
{
    private readonly VehicleContextService _context;
    [ObservableProperty] private VehicleKind selectedKind;

    public VehicleTypeSelectionViewModel(VehicleContextService context)
    {
        _context = context;
        SelectedKind = context.Current;
        Title = "ავტომობილის ტიპი";
    }

    [RelayCommand]
    private async Task Select(string kindName)
    {
        try
        {
        if (!Enum.TryParse<VehicleKind>(kindName, true, out var kind)) return;
        SelectedKind = kind;
        _context.SetVehicleKind(kind);
        await CarScanner.Services.SafeNavigation.GoToAsync("..");
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "VehicleTypeSelectionViewModel.Select");
        }
    }
}
