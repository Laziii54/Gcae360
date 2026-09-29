using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CarScanner.VinCheck.Services;
using SQLite;

namespace CarScanner.VinCheck.Models;

public sealed class VinReport
{
    public string Vin { get; set; } = "";
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Trim { get; set; }
    public string? BodyType { get; set; }
    public string? Engine { get; set; }
    public string? FuelType { get; set; }
    public string? Transmission { get; set; }
    public string? DriveType { get; set; }
    public string? ExteriorColor { get; set; }
    public string? InteriorColor { get; set; }
    public string? VehicleImageUrl { get; set; }
    public string? ReportUrl { get; set; }
    public List<VehicleSpecification> Specifications { get; set; } = [];
    public List<HistoryAlert> Alerts { get; set; } = [];
    public List<AuctionRecord> AuctionRecords { get; set; } = [];
    public List<AccidentRecord> AccidentRecords { get; set; } = [];
    public List<MileageRecord> MileageRecords { get; set; } = [];
    public List<TitleRecord> TitleRecords { get; set; } = [];
    public List<OwnershipRecord> OwnershipRecords { get; set; } = [];
    public List<TheftRecord> TheftRecords { get; set; } = [];
    public List<ServiceRecord> ServiceRecords { get; set; } = [];
    public List<RecallRecord> RecallRecords { get; set; } = [];

    /// <summary>Pictures of this VIN found on the open web (other sites).</summary>
    public List<WebPhoto> WebPhotos { get; set; } = [];

    /// <summary>Google image search for this VIN.</summary>
    public string? WebSearchUrl { get; set; }

    /// <summary>Owner only: which data source answered what.</summary>
    public List<SourceTrace>? Sources { get; set; }

    /// <summary>Lock state and VIN allowance sent by the server.</summary>
    public VinAccessInfo? Access { get; set; }

    public string DisplayName => string.Join(" ", new[] { Make, Model }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string Subtitle => string.Join(" • ", new[] { Year?.ToString(), Trim, FuelType }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public bool HasHistory =>
        Alerts.Count + AuctionRecords.Count + AccidentRecords.Count + MileageRecords.Count + TitleRecords.Count +
        OwnershipRecords.Count + TheftRecords.Count + ServiceRecords.Count + RecallRecords.Count > 0;
}

/// <summary>History preview / lock state of one report.</summary>
public sealed class VinAccessInfo
{
    /// <summary>ready, deferred (checked when opened) or unavailable.</summary>
    public string HistoryState { get; set; } = "ready";
    public bool HistoryLocked { get; set; }
    public int AuctionCount { get; set; }
    public int PhotoCount { get; set; }
    public bool HasVinPlan { get; set; }
    public int MonthlyLimit { get; set; }
    public int MonthlyUsed { get; set; }
    public int Remaining { get; set; }
    public int Credits { get; set; }
    public DateTimeOffset? ResetsAt { get; set; }
    public bool Unlocked { get; set; }
}

/// <summary>Monthly VIN allowance of the account (/api/vin/quota).</summary>
public sealed class VinQuotaInfo
{
    public bool HasVinPlan { get; set; }
    public int MonthlyLimit { get; set; }
    public int MonthlyUsed { get; set; }
    public int Remaining { get; set; }
    public int Credits { get; set; }
    public DateTimeOffset? ResetsAt { get; set; }
}

public sealed class VehicleSpecification
{
    /// <summary>Language-neutral name from the server (e.g. BodyClass).</summary>
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class HistoryAlert
{
    public string Label { get; set; } = "";
    public string Status { get; set; } = "unknown";
    public string Detail { get; set; } = "";
    public string Icon => Status == "issue" ? "!" : Status == "clear" ? "✓" : "?";
    public Color Color => Status == "issue" ? Color.FromArgb("#EF4444") : Status == "clear" ? Color.FromArgb("#22C55E") : Color.FromArgb("#94A3B8");
    /// <summary>Soft tile background of the status.</summary>
    public Color TileColor => Status == "issue" ? Color.FromArgb("#26EF4444") : Status == "clear" ? Color.FromArgb("#2622C55E") : Color.FromArgb("#1A94A3B8");
}

public sealed class AuctionRecord
{
    public string? AuctionName { get; set; }
    public DateTime? SaleDate { get; set; }
    public string? LotNumber { get; set; }
    public string? Location { get; set; }
    public string? Seller { get; set; }
    public string? SaleType { get; set; }
    public string? PrimaryDamage { get; set; }
    public string? SecondaryDamage { get; set; }
    public string? Condition { get; set; }
    public decimal? OdometerValue { get; set; }
    public string? OdometerUnit { get; set; }
    public string? OdometerStatus { get; set; }
    public Money? EstimatedRetailValue { get; set; }
    public Money? RepairCost { get; set; }
    public Money? SoldPrice { get; set; }
    public bool? KeysAvailable { get; set; }
    public bool? RunAndDrive { get; set; }
    public bool? EngineStarts { get; set; }
    public string? TitleStatus { get; set; }
    public string? Vin { get; set; }
    public List<string> ImageUrls { get; set; } = [];

    public string Header => $"{AuctionName ?? "Auction"} • {(SaleDate?.Year.ToString() ?? "—")}";
    public string DateText => SaleDate?.ToString("d") ?? "—";
    public string OdometerText => OdometerValue is null ? "—" : $"{OdometerValue:N0} {OdometerUnit}";
    public string PriceText => SoldPrice?.Formatted ?? "—";

    public string LotText => VinText.Labeled("Lot", LotNumber);
    public string SaleText => VinText.Labeled("Sale", DateText);
    public string PrimaryText => VinText.Labeled("Primary", PrimaryDamage);
    public string SecondaryText => VinText.Labeled("Secondary", SecondaryDamage);
    public string OdometerLineText => VinText.Labeled("Odometer", OdometerText);
    public string TitleText => VinText.Labeled("Title", TitleStatus);

    public IEnumerable<string> UniqueImages =>
        ImageUrls.Where(x => Uri.TryCreate(x, UriKind.Absolute, out _)).Distinct(StringComparer.OrdinalIgnoreCase);

    public string? MainImage => UniqueImages.FirstOrDefault();
    public bool HasImages => MainImage is not null;
    public string PhotoCountText => VinText.Format("PhotoCount", UniqueImages.Count());
    public string AuctionTitle => AuctionName ?? VinText.T("Auction");
    public string SaleDateText => SaleDate?.ToString("dd.MM.yyyy") ?? "—";
    public bool HasPrice => SoldPrice is not null;
    public string DamageValue => string.IsNullOrWhiteSpace(PrimaryDamage) ? "—" : PrimaryDamage!;
    public string SecondaryValue => string.IsNullOrWhiteSpace(SecondaryDamage) ? "—" : SecondaryDamage!;
    public string TitleValue => string.IsNullOrWhiteSpace(TitleStatus) ? "—" : TitleStatus!;
    public string LotValue => string.IsNullOrWhiteSpace(LotNumber) ? "—" : LotNumber!;
    public string KeysValue => KeysAvailable is null ? "—" : VinText.T(KeysAvailable.Value ? "Yes" : "No");
    public string RunValue => RunAndDrive is null ? (string.IsNullOrWhiteSpace(Condition) ? "—" : Condition!) : VinText.T(RunAndDrive.Value ? "Yes" : "No");
}

public sealed class WebPhoto
{
    public string ThumbnailUrl { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string? PageUrl { get; set; }
    public string? Source { get; set; }
}

/// <summary>Paid full history offer / report (/api/vin/{vin}/full).</summary>
public sealed class FullHistoryInfo
{
    public bool Configured { get; set; }
    public bool Available { get; set; }
    public string? Reason { get; set; }
    public bool Purchased { get; set; }
    public bool Pending { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "GEL";
    public FullHistoryReport? Report { get; set; }
}

public sealed class FullHistoryReport
{
    public bool Clean { get; set; }
    public string Summary { get; set; } = "";
    public string? LastOdometer { get; set; }
    public int TitleRecords { get; set; }
    public List<FullHistorySection> Sections { get; set; } = [];
}

public sealed class FullHistorySection
{
    public string Title { get; set; } = "";
    public bool Warning { get; set; }
    public List<FullHistoryItem> Items { get; set; } = [];
}

public sealed class FullHistoryItem
{
    public string? Date { get; set; }
    public string Title { get; set; } = "";
    public string? Detail { get; set; }
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public bool HasDate => !string.IsNullOrWhiteSpace(Date);
}

/// <summary>Answer of /api/vin/{vin}/full/checkout.</summary>
public sealed class FullCheckoutResult
{
    public bool Success { get; set; }
    public bool Purchased { get; set; }
    public string? RedirectUrl { get; set; }
    public string? Code { get; set; }
}

public sealed class SourceTrace
{
    public string Provider { get; set; } = "";
    public int Status { get; set; }
    public int Items { get; set; }
    public string? Note { get; set; }

    public string Line =>
        $"{Provider}: " + (Status == 0 ? "—" : Status.ToString()) + $" · {Items}" + (string.IsNullOrWhiteSpace(Note) ? "" : $" · {Note}");
}

public sealed class Money
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Formatted => Currency == "USD" ? $"${Amount:N0}" : $"{Amount:N0} {Currency}";
}

public sealed class AccidentRecord
{
    public DateTime? Date { get; set; }
    public string? Description { get; set; }
    public string? Impact { get; set; }
    public string? Severity { get; set; }
    public string DateText => Date?.ToString("d") ?? "—";
    public string SeverityText => VinText.Labeled("Severity", Severity);
}

public sealed class MileageRecord
{
    public DateTime? Date { get; set; }
    public decimal? OdometerValue { get; set; }
    public string? OdometerUnit { get; set; }
    public string? Source { get; set; }
    public bool IsPossibleInconsistency { get; set; }
    public string ValueText => OdometerValue is null ? "—" : $"{OdometerValue:N0} {OdometerUnit}";
    public string DateText => Date?.ToString("yyyy") ?? "—";
}

public sealed class TitleRecord
{
    public DateTime? Date { get; set; }
    public string? Status { get; set; }
    public string? State { get; set; }
    public string DateText => Date?.ToString("d") ?? "—";
}

public sealed class OwnershipRecord
{
    public int Number { get; set; }
    public string? OwnerType { get; set; }
    public string? State { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string Period => $"{From?.Year}–{To?.Year}";
}

public sealed class TheftRecord
{
    public DateTime? ReportedDate { get; set; }
    public DateTime? RecoveryDate { get; set; }
    public string? Country { get; set; }
}

public sealed class ServiceRecord
{
    public DateTime? Date { get; set; }
    public decimal? Mileage { get; set; }
    public string? Unit { get; set; }
    public string? ServiceType { get; set; }
    public string? ServiceCenter { get; set; }
    public string? Description { get; set; }
    public string DateText => Date?.ToString("d") ?? "—";
    public string MileageText => Mileage is null ? "" : $"{Mileage:N0} {Unit}";
}

public sealed class RecallRecord : INotifyPropertyChanged
{
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Description shown or hidden (screen only, not saved).</summary>
    [JsonIgnore]
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            Changed();
            Changed(nameof(Chevron));
        }
    }

    [JsonIgnore] public string Chevron => IsExpanded ? "▲" : "▼";
    [JsonIgnore] public string CategoryText => VinText.RecallCategory(Component);
    [JsonIgnore] public string DateText => Date?.ToString("dd.MM.yyyy") ?? "";

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string? RecallId { get; set; }
    public DateTime? Date { get; set; }
    public string? Component { get; set; }
    public string? Description { get; set; }
    public string? Manufacturer { get; set; }
    public string? Remedy { get; set; }
    public string RemedyText => VinText.Labeled("Remedy", Remedy);
}

public sealed class VinSearchHistory
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public string Vin { get; set; } = "";
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? ThumbnailUrl { get; set; }
    public DateTime SearchDate { get; set; }
    public string ReportJson { get; set; } = "";
    [Ignore] public string DisplayName => string.Join(" ", new[] { Make, Model }.Where(x => !string.IsNullOrWhiteSpace(x)));
    [Ignore] public string DateText => SearchDate.ToString("g");
}

public sealed class SavedVehicle
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Unique] public string Vin { get; set; } = "";
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime SavedDate { get; set; }
    public string ReportJson { get; set; } = "";
}
