using System.Collections.ObjectModel;
using CarScanner.Models;
using CarScanner.Services.ServiceProcedures;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CarScanner.ViewModels;

/// <summary>One procedure on the Service page.</summary>
public sealed partial class ServiceProcedureRow : ObservableObject
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string KindText { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string StatusColor { get; init; } = "#7A8594";
    public string StatusBackground => "#22" + StatusColor.TrimStart('#');
    public string MethodText { get; init; } = "";
    public string MethodDetailText { get; init; } = "";
    public string ReasonsText { get; init; } = "";
    public bool HasReasons => ReasonsText.Length > 0;
    public string MissingText { get; init; } = "";
    public bool HasMissing => MissingText.Length > 0;
    public string PrerequisitesText { get; init; } = "";
    public string StepsText { get; init; } = "";
    public bool HasSteps => StepsText.Length > 0;
    public string ExpectedText { get; init; } = "";
    public string VerificationText { get; init; } = "";
    public string VerificationColor { get; init; } = "#7A8594";
    public string SourceText { get; init; } = "";
    public bool HasSource => SourceText.Length > 0;

    /// <summary>True only for the reviewed on-device reset that really exists in the app.</summary>
    public bool CanRunAutomatic { get; init; }
    public string RunButtonText { get; init; } = "";

    [ObservableProperty] private bool isExpanded;

    public string ChevronText => IsExpanded ? "⌃" : "⌄";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ChevronText));
}

/// <summary>A category chip above the list.</summary>
public sealed partial class ServiceKindChip : ObservableObject
{
    public ServiceKind? Kind { get; init; }
    [ObservableProperty] private string title = "";
    [ObservableProperty] private bool isSelected;

    public string BackgroundColor => IsSelected ? "#1D5BD6" : "#00000000";
    public string TextColorValue => IsSelected ? "#FFFFFF" : "#7A8594";

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(TextColorValue));
    }
}

/// <summary>
/// Service page: vehicle card, search, categories and the procedure list with an honest compatibility status for
/// the connected vehicle. It reuses the existing catalog, the existing oil-life reset and the existing guides —
/// it adds no new way to talk to the car.
/// </summary>
public partial class ServiceViewModel
{
    private const string BuiltInOilResetId = "builtin.oil-life-reset";

    private ServiceVehicleFacts _procedureFacts = ServiceVehicleFacts.From(null);
    private CancellationTokenSource? _procedureSearchDelay;

    public ObservableCollection<ServiceProcedureRow> ProcedureRows { get; } = new();
    public ObservableCollection<ServiceKindChip> KindChips { get; } = new();

    [ObservableProperty] private string procedureSearchText = "";
    [ObservableProperty] private ServiceKind? selectedKind;
    [ObservableProperty] private bool isLoadingProcedures;
    [ObservableProperty] private string procedureEmptyText = "";
    [ObservableProperty] private string procedureErrorText = "";
    [ObservableProperty] private string vehicleLineText = "";
    [ObservableProperty] private string vehicleDetailText = "";
    [ObservableProperty] private string vehicleMissingText = "";

    public bool HasProcedureRows => ProcedureRows.Count > 0;
    public bool HasProcedureEmpty => ProcedureRows.Count == 0 && !IsLoadingProcedures && ProcedureEmptyText.Length > 0;
    public bool HasProcedureError => ProcedureErrorText.Length > 0;
    public bool HasVehicleMissing => VehicleMissingText.Length > 0;
    public bool HasVehicleDetail => VehicleDetailText.Length > 0;
    public bool HasProcedureSearch => ProcedureSearchText.Length > 0;
    public bool ShowKindChips => KindChips.Count > 2;

    public string ProceduresTitleText => L("სერვისის პროცედურები", "Service procedures");
    public string ProcedureSearchPlaceholder => L("მოძებნე პროცედურა, მარკა ან სისტემა", "Search procedure, brand or system");
    public string ProcedureAllText => L("ყველა", "All");

    partial void OnProcedureSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasProcedureSearch));
        _ = DebouncedRebuildProceduresAsync();
    }

    partial void OnVehicleDetailTextChanged(string value) => OnPropertyChanged(nameof(HasVehicleDetail));

    partial void OnSelectedKindChanged(ServiceKind? value)
    {
        foreach (var chip in KindChips)
            chip.IsSelected = chip.Kind == value;
        RebuildProcedureRows();
    }

    partial void OnIsLoadingProceduresChanged(bool value) => OnPropertyChanged(nameof(HasProcedureEmpty));

    partial void OnProcedureEmptyTextChanged(string value) => OnPropertyChanged(nameof(HasProcedureEmpty));

    partial void OnProcedureErrorTextChanged(string value) => OnPropertyChanged(nameof(HasProcedureError));

    partial void OnVehicleMissingTextChanged(string value) => OnPropertyChanged(nameof(HasVehicleMissing));

    private async Task DebouncedRebuildProceduresAsync()
    {
        _procedureSearchDelay?.Cancel();
        var delay = _procedureSearchDelay = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, delay.Token);
            RebuildProcedureRows();
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Loads the catalog (if needed), evaluates it for the connected vehicle and rebuilds the list.</summary>
    private async Task RefreshProcedureListAsync(CancellationToken token)
    {
        IsLoadingProcedures = true;
        ProcedureErrorText = "";
        try
        {
            await _serviceProcedureCatalog.EnsureLoadedAsync(token);

            var vehicle = _currentOemVehicle ?? _vehicle.CurrentVehicle;
            var selectedBrand = _brandProfiles.Current;
            var fallbackBrand = selectedBrand.Id.Equals("other", StringComparison.OrdinalIgnoreCase) ? null : selectedBrand.DisplayName;

            // Only what the adapter has really negotiated: CAN means UDS-style module access is possible.
            var capabilities = _obd.IsInitialized && _obd.IsCanProtocol ? new[] { "can-uds" } : Array.Empty<string>();
            _procedureFacts = ServiceVehicleFacts.From(vehicle, fallbackBrand, capabilities);

            UpdateVehicleCard(vehicle, fallbackBrand);
            RebuildProcedureRows();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ProcedureErrorText = L(
                $"პროცედურების სია ვერ ჩაიტვირთა: {CarScanner.Services.UserError.Describe(ex)}",
                $"The procedure list could not be loaded: {CarScanner.Services.UserError.Describe(ex)}");
            ProcedureRows.Clear();
        }
        finally
        {
            IsLoadingProcedures = false;
            OnPropertyChanged(nameof(HasProcedureRows));
            OnPropertyChanged(nameof(HasProcedureEmpty));
        }
    }

    private void UpdateVehicleCard(VehicleInfo? vehicle, string? fallbackBrand)
    {
        static string Known(string? v) => string.IsNullOrWhiteSpace(v) || v.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? "" : v.Trim();

        var make = Known(vehicle?.Manufacturer);
        if (make.Length == 0)
            make = Known(fallbackBrand);
        var line = string.Join(" ", new[] { make, Known(vehicle?.Model), Known(vehicle?.Year) }.Where(s => s.Length > 0));
        VehicleLineText = line.Length > 0 ? line : L("მანქანა ამოცნობილი არ არის", "Vehicle not identified");

        VehicleDetailText = string.Join(" · ", new[] { Known(vehicle?.Engine), Known(vehicle?.FuelType), Known(vehicle?.Protocol) }.Where(s => s.Length > 0));

        var missing = new List<string>();
        if (!_procedureFacts.HasBrand) missing.Add(L("მწარმოებელი", "manufacturer"));
        if (_procedureFacts.Model.Length == 0) missing.Add(L("მოდელი", "model"));
        if (_procedureFacts.Year is null) missing.Add(L("წელი", "model year"));
        VehicleMissingText = missing.Count == 0
            ? ""
            : L($"დასაზუსტებელია: {string.Join(", ", missing)}. ამის გარეშე პროცედურები ნაწილობრივ მხარდაჭერილად ჩანს.",
                $"Missing: {string.Join(", ", missing)}. Until it is known, procedures can only be shown as partially supported.");
    }

    private void RebuildProcedureRows()
    {
        try
        {
            var en = _localization.IsEnglish;
            var catalog = _serviceProcedureCatalog.All.ToList();

            // The reviewed on-device oil-life reset is offered by the oil card; it is listed here with its real status.
            ServiceProcedureDefinition? builtIn = null;
            if (CanResetOilLife)
            {
                builtIn = new ServiceProcedureDefinition
                {
                    Id = BuiltInOilResetId,
                    Brand = "*",
                    Model = "*",
                    Kind = nameof(ServiceKind.OilService),
                    NameEn = "Oil life reset (through the diagnostic interface)",
                    NameKa = "ზეთის რესურსის განულება (დიაგნოსტიკური ინტერფეისით)",
                    DescriptionEn = "Writes the oil-life value to the car and reads it back to confirm.",
                    DescriptionKa = "ზეთის რესურსის მნიშვნელობას მანქანაში წერს და დასადასტურებლად თავიდან კითხულობს.",
                    ExecutionType = ServiceProcedureExecutionType.OemDiagnostic,
                    OemOperationId = "builtin:oil-life",
                    VerificationMethod = "ReadBack",
                    Source = "GCar360 reviewed reset path for this vehicle"
                };
                catalog.Insert(0, builtIn);
            }

            var all = ServiceProcedureCatalogView.Build(
                catalog, _procedureFacts, ProcedureSearchText, null,
                p => p.Id == BuiltInOilResetId && CanResetOilLife);

            RebuildChips(all, en);

            var shown = SelectedKind is { } kind ? all.Where(e => e.Kind == kind).ToList() : all;

            ProcedureRows.Clear();
            foreach (var entry in shown)
                ProcedureRows.Add(ToRow(entry, en));

            if (ProcedureRows.Count == 0)
            {
                ProcedureEmptyText = !_procedureFacts.HasBrand && string.IsNullOrWhiteSpace(ProcedureSearchText)
                    ? L("მანქანა ამოცნობილი არ არის. დაუკავშირდი მანქანას ან მოძებნე პროცედურა სახელით.",
                        "The vehicle is not identified. Connect to the car, or search for a procedure by name.")
                    : string.IsNullOrWhiteSpace(ProcedureSearchText)
                        ? L("ამ მანქანისთვის დოკუმენტირებული პროცედურა კატალოგში ჯერ არ არის. ეს არ ნიშნავს, რომ ასეთი პროცედურა არ არსებობს.",
                            "No documented procedure for this car is in the catalog yet. That does not mean none exists.")
                        : L("ვერაფერი მოიძებნა.", "Nothing found.");
            }
            else
            {
                ProcedureEmptyText = "";
            }
        }
        catch (Exception ex)
        {
            ProcedureErrorText = L(
                $"სიის აგება ვერ მოხერხდა: {CarScanner.Services.UserError.Describe(ex)}",
                $"The list could not be built: {CarScanner.Services.UserError.Describe(ex)}");
        }
        finally
        {
            OnPropertyChanged(nameof(HasProcedureRows));
            OnPropertyChanged(nameof(HasProcedureEmpty));
        }
    }

    private void RebuildChips(IReadOnlyList<ServiceProcedureEntry> all, bool en)
    {
        KindChips.Clear();
        KindChips.Add(new ServiceKindChip { Kind = null, Title = $"{ProcedureAllText} {all.Count}", IsSelected = SelectedKind is null });
        foreach (var group in all.GroupBy(e => e.Kind).OrderBy(g => g.Key))
        {
            KindChips.Add(new ServiceKindChip
            {
                Kind = group.Key,
                Title = $"{ServiceProcedureTexts.Kind(group.Key, en)} {group.Count()}",
                IsSelected = SelectedKind == group.Key
            });
        }

        // A chip that no longer exists (e.g. after a search) must not leave the list filtered to nothing.
        if (SelectedKind is { } selected && KindChips.All(c => c.Kind != selected))
        {
            SelectedKind = null;
            return;
        }

        OnPropertyChanged(nameof(ShowKindChips));
    }

    private ServiceProcedureRow ToRow(ServiceProcedureEntry entry, bool en)
    {
        var p = entry.Definition;
        var c = entry.Compatibility;
        var builtIn = p.Id == BuiltInOilResetId;

        string Pick(string ka, string eng) => en ? (string.IsNullOrWhiteSpace(eng) ? ka : eng) : (string.IsNullOrWhiteSpace(ka) ? eng : ka);

        var years = p.MinimumYear > 0 || p.MaximumYear > 0
            ? $"{(p.MinimumYear > 0 ? p.MinimumYear.ToString() : "…")}–{(p.MaximumYear > 0 ? p.MaximumYear.ToString() : "…")}"
            : "";
        var model = p.Model == "*" ? "" : p.Model;
        var brand = p.Brand == "*" ? L("ზოგადი", "General") : p.Brand.Replace("|", " / ");
        var subtitle = string.Join(" · ", new[] { brand, model, years, p.SystemName }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var steps = string.Join(Environment.NewLine,
            p.Steps.OrderBy(s => s.Order).Select(s => $"{s.Order}. {Pick(s.TextKa, s.TextEn)}"));

        var preconditions = new List<string>();
        preconditions.Add(c.Method == ServiceMethod.Automatic
            ? ServiceProcedureTexts.AutomaticPrerequisites(en)
            : ServiceProcedureTexts.ManualPrerequisites(en));
        preconditions.AddRange(p.Preconditions.Select(t => Pick(t.TextKa, t.TextEn)).Where(t => t.Length > 0));

        var expected = Pick(p.ExpectedResultKa, p.ExpectedResultEn);
        if (expected.Length == 0)
        {
            expected = p.VerificationMethod.Equals("ReadBack", StringComparison.OrdinalIgnoreCase)
                ? ServiceProcedureTexts.ReadBackVerification(en)
                : ServiceProcedureTexts.DashboardVerification(en);
        }

        var runnable = builtIn && c.Status == ServiceCompatibilityStatus.Supported && CanResetOilLife;

        return new ServiceProcedureRow
        {
            Id = p.Id,
            Title = Pick(p.NameKa, p.NameEn),
            Subtitle = subtitle,
            KindText = ServiceProcedureTexts.Kind(entry.Kind, en),
            StatusText = ServiceProcedureTexts.Status(c.Status, en),
            StatusColor = ServiceProcedureTexts.StatusColor(c.Status),
            MethodText = ServiceProcedureTexts.MethodShort(c.Method, en),
            MethodDetailText = ServiceProcedureTexts.Method(c.Method, en),
            ReasonsText = string.Join(Environment.NewLine, c.Reasons.Select(r => ServiceProcedureTexts.Reason(r, en))),
            MissingText = string.Join(" ", c.Missing.Select(r => ServiceProcedureTexts.Reason(r, en))),
            PrerequisitesText = string.Join(Environment.NewLine, preconditions),
            StepsText = steps,
            ExpectedText = expected,
            VerificationText = ServiceProcedureTexts.Verification(entry.Verification, en),
            VerificationColor = ServiceProcedureTexts.VerificationColor(entry.Verification),
            SourceText = p.Source,
            CanRunAutomatic = runnable,
            RunButtonText = L("განულება", "Reset")
        };
    }

    [RelayCommand]
    private void ToggleProcedure(ServiceProcedureRow? row)
    {
        if (row is null)
            return;

        var expanding = !row.IsExpanded;
        foreach (var other in ProcedureRows.Where(r => r.IsExpanded))
            other.IsExpanded = false;
        row.IsExpanded = expanding;
    }

    [RelayCommand]
    private void SelectKind(ServiceKindChip? chip)
    {
        if (chip is null)
            return;
        SelectedKind = chip.Kind;
    }

    [RelayCommand]
    private void ClearProcedureSearch() => ProcedureSearchText = "";

    /// <summary>The same reviewed command as the oil card (it asks for confirmation and checks speed/voltage first).</summary>
    [RelayCommand]
    private Task RunProcedure(ServiceProcedureRow? row)
    {
        if (row is null || !row.CanRunAutomatic || row.Id != BuiltInOilResetId)
            return Task.CompletedTask;

        return ResetOilLifeFullCommand.ExecuteAsync(null);
    }
}
