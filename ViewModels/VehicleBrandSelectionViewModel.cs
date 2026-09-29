using CarScanner.Models;
using CarScanner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CarScanner.ViewModels;

public partial class VehicleBrandSelectionViewModel : BaseViewModel
{
    private readonly VehicleBrandProfileService _brands;

    public ObservableCollection<VehicleBrandProfile> Brands { get; }

    [ObservableProperty]
    private string searchText = string.Empty;

    public VehicleBrandSelectionViewModel(VehicleBrandProfileService brands)
    {
        _brands = brands;
        Brands = new ObservableCollection<VehicleBrandProfile>(_brands.Available);
        Title = "აირჩიე მარკა";
    }

    partial void OnSearchTextChanged(string value)
    {
        var query = value?.Trim() ?? string.Empty;
        var matches = string.IsNullOrWhiteSpace(query)
            ? _brands.Available
            : _brands.Available.Where(brand =>
                brand.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                brand.Mark.Contains(query, StringComparison.OrdinalIgnoreCase));

        Brands.Clear();
        foreach (var brand in matches)
            Brands.Add(brand);
    }

    [RelayCommand]
    private async Task SelectBrand(VehicleBrandProfile? brand)
    {
        if (brand is null)
            return;

        _brands.Select(brand.Id);
        await Shell.Current.GoToAsync(nameof(Views.BluetoothPage));
    }
}
