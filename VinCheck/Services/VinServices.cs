using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarScanner.VinCheck.Models;
using SQLite;

namespace CarScanner.VinCheck.Services;

public interface IVinValidatorService
{
    string Normalize(string? value);
    bool IsValid(string? value, out string error);
}

public sealed partial class VinValidatorService : IVinValidatorService
{
    [GeneratedRegex("[^A-HJ-NPR-Z0-9]")]
    private static partial Regex InvalidChars();

    [GeneratedRegex("^[A-HJ-NPR-Z0-9]{17}$")]
    private static partial Regex ValidVin();

    /// <summary>
    /// Upper-case, no spaces, max 17 characters. I, O and Q never appear in a
    /// VIN, so the usual typos are corrected (O/Q → 0, I → 1).
    /// </summary>
    public string Normalize(string? value)
    {
        var text = (value ?? "").ToUpperInvariant()
            .Replace('O', '0')
            .Replace('Q', '0')
            .Replace('I', '1');
        text = InvalidChars().Replace(text, "");
        return text.Length > 17 ? text[..17] : text;
    }

    public bool IsValid(string? value, out string error)
    {
        var vin = (value ?? "").Trim().ToUpperInvariant();
        if (vin.Length != 17) { error = VinText.T("VinLength"); return false; }
        if (!ValidVin().IsMatch(vin)) { error = VinText.T("VinInvalidChars"); return false; }
        error = "";
        return true;
    }
}

public sealed class VinServiceException(string message, VinError kind, VinQuotaInfo? quota = null) : Exception(message)
{
    public VinError Kind { get; } = kind;

    /// <summary>The allowance at the moment of the refusal (plan / quota errors).</summary>
    public VinQuotaInfo? Quota { get; } = quota;
}

public enum VinError { Invalid, NotFound, Offline, Unauthorized, RateLimited, Unavailable, Malformed, SignInRequired, PlanRequired, QuotaExceeded }

public interface IVinHistoryService
{
    /// <summary>Specs + recalls, and the history preview (full if already opened).</summary>
    Task<VinReport> GetReportAsync(string vin, CancellationToken cancellationToken);

    /// <summary>Opens the full history. Uses 1 VIN of the month (or a credit).</summary>
    Task<VinReport> UnlockAsync(string vin, CancellationToken cancellationToken);

    /// <summary>Monthly allowance and extra credits; null when unknown.</summary>
    Task<VinQuotaInfo?> GetQuotaAsync(CancellationToken cancellationToken);

    /// <summary>Paid full history: the offer (price) or the bought report. Null when unknown.</summary>
    Task<FullHistoryInfo?> GetFullHistoryAsync(string vin, CancellationToken cancellationToken);

    /// <summary>Starts the bank payment for the full history.</summary>
    Task<FullCheckoutResult> StartFullCheckoutAsync(string vin, CancellationToken cancellationToken);
}

/// <summary>
/// Real VIN history through the OBD360 backend. The provider key stays on
/// the server; the server also counts the monthly VIN allowance.
/// </summary>
public sealed class VinHistoryService(CarScanner.Services.AccountService account) : IVinHistoryService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(45) };

    public Task<VinReport> GetReportAsync(string vin, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, $"/api/vin/{Uri.EscapeDataString(vin)}", cancellationToken);

    public Task<VinReport> UnlockAsync(string vin, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"/api/vin/{Uri.EscapeDataString(vin)}/unlock", cancellationToken);

    public async Task<VinQuotaInfo?> GetQuotaAsync(CancellationToken cancellationToken)
    {
        if (!account.IsSignedIn)
            return null;

        try
        {
            using var request = await account.CreateAuthorizedRequestAsync(HttpMethod.Get, "/api/vin/quota");
            using var response = await _client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<VinQuotaInfo>(Json, cancellationToken)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<FullHistoryInfo?> GetFullHistoryAsync(string vin, CancellationToken cancellationToken)
    {
        if (!account.IsSignedIn)
            return null;

        try
        {
            using var request = await account.CreateAuthorizedRequestAsync(HttpMethod.Get, $"/api/vin/{Uri.EscapeDataString(vin)}/full");
            using var response = await _client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<FullHistoryInfo>(Json, cancellationToken)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<FullCheckoutResult> StartFullCheckoutAsync(string vin, CancellationToken cancellationToken)
    {
        if (!account.IsSignedIn)
            return new FullCheckoutResult { Code = "SIGN_IN" };

        try
        {
            using var request = await account.CreateAuthorizedRequestAsync(HttpMethod.Post, $"/api/vin/{Uri.EscapeDataString(vin)}/full/checkout");
            request.Content = JsonContent.Create(new { provider = (string?)null });
            using var response = await _client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new FullCheckoutResult { Code = "SIGN_IN" };
            return await response.Content.ReadFromJsonAsync<FullCheckoutResult>(Json, cancellationToken)
                   ?? new FullCheckoutResult { Code = "EMPTY" };
        }
        catch
        {
            return new FullCheckoutResult { Code = "OFFLINE" };
        }
    }

    private async Task<VinReport> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        try
        {
            using var request = await account.CreateAuthorizedRequestAsync(method, path);
            using var response = await _client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.BadRequest) throw new VinServiceException("Invalid VIN", VinError.Invalid);
            if (response.StatusCode == HttpStatusCode.NotFound) throw new VinServiceException("Vehicle not found", VinError.NotFound);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new VinServiceException("Sign in required", method == HttpMethod.Post ? VinError.SignInRequired : VinError.Unauthorized);
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new VinServiceException("Service unavailable", VinError.Unauthorized);
            if (response.StatusCode == HttpStatusCode.PaymentRequired)
            {
                var refusal = await ReadRefusalAsync(response, cancellationToken);
                throw new VinServiceException(
                    "VIN plan required",
                    refusal?.Code == "QUOTA_EXCEEDED" ? VinError.QuotaExceeded : VinError.PlanRequired,
                    refusal?.Quota);
            }
            if ((int)response.StatusCode == 429) throw new VinServiceException("Too many requests", VinError.RateLimited);
            if ((int)response.StatusCode >= 500) throw new VinServiceException("Service unavailable", VinError.Unavailable);
            response.EnsureSuccessStatusCode();

            var report = await response.Content.ReadFromJsonAsync<VinReport>(Json, cancellationToken)
                         ?? throw new VinServiceException("Empty response", VinError.Malformed);
            VinReportPresenter.Prepare(report);
            return report;
        }
        catch (VinServiceException) { throw; }
        catch (InvalidOperationException) { throw new VinServiceException("Backend unavailable", VinError.Unavailable); }
        catch (HttpRequestException) { throw new VinServiceException("Unable to connect", VinError.Offline); }
        catch (JsonException) { throw new VinServiceException("Malformed response", VinError.Malformed); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VinServiceException("Request timed out", VinError.Unavailable);
        }
    }

    private static async Task<VinRefusal?> ReadRefusalAsync(HttpResponseMessage response, CancellationToken token)
    {
        try { return await response.Content.ReadFromJsonAsync<VinRefusal>(Json, token); }
        catch { return null; }
    }

    private sealed class VinRefusal
    {
        public string? Code { get; set; }
        public VinQuotaInfo? Quota { get; set; }
    }
}

/// <summary>Sample data until a paid VIN history provider is connected.</summary>
public sealed class MockVinHistoryService : IVinHistoryService
{
    public Task<VinReport> UnlockAsync(string vin, CancellationToken cancellationToken) =>
        GetReportAsync(vin, cancellationToken);

    public Task<FullHistoryInfo?> GetFullHistoryAsync(string vin, CancellationToken cancellationToken) =>
        Task.FromResult<FullHistoryInfo?>(null);

    public Task<FullCheckoutResult> StartFullCheckoutAsync(string vin, CancellationToken cancellationToken) =>
        Task.FromResult(new FullCheckoutResult { Code = "NOT_AVAILABLE" });

    public Task<VinQuotaInfo?> GetQuotaAsync(CancellationToken cancellationToken) =>
        Task.FromResult<VinQuotaInfo?>(null);

    public async Task<VinReport> GetReportAsync(string vin, CancellationToken cancellationToken)
    {
        await Task.Delay(850, cancellationToken);
        var images = Enumerable.Range(10, 14).Select(i => $"https://picsum.photos/seed/gcar360-{i}/1200/800").ToList();
        var report = new VinReport
        {
            Vin = vin, Make = "Chevrolet", Model = "Volt", Year = 2012, Trim = "Premium", BodyType = "Hatchback",
            Engine = "1.4L + Electric", FuelType = "Plug-in Hybrid", Transmission = "Automatic", DriveType = "FWD",
            ExteriorColor = "Silver", InteriorColor = "Black", VehicleImageUrl = images[0]
        };
        report.Specifications =
        [
            new() { Label = "Manufacturer", Value = "General Motors LLC" }, new() { Label = "Make", Value = "Chevrolet" },
            new() { Label = "Model", Value = "Volt" }, new() { Label = "Model Year", Value = "2012" },
            new() { Label = "Engine", Value = "1.4L + Electric" }, new() { Label = "Cylinders", Value = "4" },
            new() { Label = "Fuel", Value = "Plug-in Hybrid" }, new() { Label = "Transmission", Value = "Automatic" },
            new() { Label = "Drivetrain", Value = "FWD" }, new() { Label = "Doors", Value = "4" },
            new() { Label = "Seats", Value = "4" }, new() { Label = "Country", Value = "United States" },
            new() { Label = "Plant", Value = "Detroit Hamtramck" }
        ];
        report.Alerts =
        [
            new() { Label = "Accidents", Status = "issue", Detail = "1 record" },
            new() { Label = "Mileage", Status = "clear", Detail = "No inconsistency detected" },
            new() { Label = "Title", Status = "issue", Detail = "Salvage record" },
            new() { Label = "Theft", Status = "clear", Detail = "No theft records" },
            new() { Label = "Flood", Status = "unknown", Detail = "No information available" }
        ];
        report.AuctionRecords =
        [
            new()
            {
                AuctionName = "Copart", SaleDate = new(2021, 5, 14), LotNumber = "54321981", Location = "New Jersey",
                Seller = "Insurance company", SaleType = "Auction", PrimaryDamage = "Front End", SecondaryDamage = "Minor Dent/Scratches",
                Condition = "Salvage", OdometerValue = 128450, OdometerUnit = "mi", OdometerStatus = "Actual",
                EstimatedRetailValue = new() { Amount = 9420 }, RepairCost = new() { Amount = 6800 }, SoldPrice = new() { Amount = 3600 },
                KeysAvailable = true, RunAndDrive = true, EngineStarts = true, TitleStatus = "Salvage", Vin = vin, ImageUrls = images
            },
            new()
            {
                AuctionName = "IAAI", SaleDate = new(2022, 2, 8), LotNumber = "31984211", Location = "Pennsylvania",
                PrimaryDamage = "Front End", OdometerValue = 131200, OdometerUnit = "mi", SoldPrice = new() { Amount = 4100 },
                KeysAvailable = true, RunAndDrive = false, EngineStarts = true, TitleStatus = "Rebuilt", Vin = vin,
                ImageUrls = images.Take(6).Reverse().ToList()
            }
        ];
        report.AccidentRecords = [new() { Date = new(2021, 4, 28), Description = "Accident reported", Impact = "Front impact", Severity = "Moderate" }];
        report.MileageRecords =
        [
            new() { Date = new(2017, 2, 1), OdometerValue = 42310, OdometerUnit = "mi", Source = "Service" },
            new() { Date = new(2019, 4, 4), OdometerValue = 78021, OdometerUnit = "mi", Source = "Inspection" },
            new() { Date = new(2021, 5, 14), OdometerValue = 128450, OdometerUnit = "mi", Source = "Auction" },
            new() { Date = new(2024, 1, 9), OdometerValue = 171220, OdometerUnit = "mi", Source = "Service" }
        ];
        report.TitleRecords =
        [
            new() { Date = new(2012, 8, 1), Status = "Clean", State = "GA" },
            new() { Date = new(2021, 5, 1), Status = "Salvage", State = "NJ" },
            new() { Date = new(2022, 6, 1), Status = "Rebuilt", State = "PA" }
        ];
        report.OwnershipRecords =
        [
            new() { Number = 1, OwnerType = "Personal vehicle", State = "Georgia", From = new(2012, 1, 1), To = new(2017, 1, 1) },
            new() { Number = 2, OwnerType = "Personal vehicle", State = "Florida", From = new(2017, 1, 1), To = new(2021, 1, 1) }
        ];
        report.ServiceRecords = [new() { Date = new(2023, 4, 18), Mileage = 145220, Unit = "mi", ServiceType = "Maintenance", Description = "Oil and filter replaced" }];
        report.RecallRecords = [new() { RecallId = "NHTSA 18V-397", Date = new(2018, 6, 14), Component = "Hybrid propulsion", Description = "Software update required", Manufacturer = "General Motors", Remedy = "Dealer software update" }];
        return report;
    }
}

public interface IVinHistoryDatabaseService
{
    Task SaveSearchAsync(VinReport report);
    Task<IReadOnlyList<VinSearchHistory>> GetHistoryAsync();
    Task DeleteHistoryAsync(int id);
    Task ClearHistoryAsync();
    Task<bool> ToggleFavoriteAsync(VinReport report);
    Task<bool> IsFavoriteAsync(string vin);
    Task<IReadOnlyList<SavedVehicle>> GetFavoritesAsync();
}

/// <summary>VIN search history and favorites, stored only on the phone.</summary>
public sealed class VinHistoryDatabaseService : IVinHistoryDatabaseService
{
    private readonly SQLiteAsyncConnection _db = new(Path.Combine(FileSystem.AppDataDirectory, "vincheck.db3"));
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private bool _ready;

    private async Task InitAsync()
    {
        if (_ready) return;
        await _db.CreateTableAsync<VinSearchHistory>();
        await _db.CreateTableAsync<SavedVehicle>();
        _ready = true;
    }

    public async Task SaveSearchAsync(VinReport r)
    {
        await InitAsync();
        // One row per VIN: a new search replaces the older one.
        await _db.ExecuteAsync("DELETE FROM VinSearchHistory WHERE Vin = ?", r.Vin);
        await _db.InsertAsync(new VinSearchHistory
        {
            Vin = r.Vin, Make = r.Make, Model = r.Model, Year = r.Year, ThumbnailUrl = r.VehicleImageUrl,
            SearchDate = DateTime.Now, ReportJson = JsonSerializer.Serialize(r, _json)
        });
    }

    public async Task<IReadOnlyList<VinSearchHistory>> GetHistoryAsync()
    {
        await InitAsync();
        return await _db.Table<VinSearchHistory>().OrderByDescending(x => x.SearchDate).ToListAsync();
    }

    public async Task DeleteHistoryAsync(int id)
    {
        await InitAsync();
        await _db.DeleteAsync<VinSearchHistory>(id);
    }

    public async Task ClearHistoryAsync()
    {
        await InitAsync();
        await _db.DeleteAllAsync<VinSearchHistory>();
    }

    public async Task<bool> ToggleFavoriteAsync(VinReport r)
    {
        await InitAsync();
        var old = await _db.Table<SavedVehicle>().Where(x => x.Vin == r.Vin).FirstOrDefaultAsync();
        if (old is not null)
        {
            await _db.DeleteAsync(old);
            return false;
        }

        await _db.InsertAsync(new SavedVehicle
        {
            Vin = r.Vin, Make = r.Make, Model = r.Model, Year = r.Year, ImageUrl = r.VehicleImageUrl,
            SavedDate = DateTime.Now, ReportJson = JsonSerializer.Serialize(r, _json)
        });
        return true;
    }

    public async Task<bool> IsFavoriteAsync(string vin)
    {
        await InitAsync();
        return await _db.Table<SavedVehicle>().Where(x => x.Vin == vin).CountAsync() > 0;
    }

    public async Task<IReadOnlyList<SavedVehicle>> GetFavoritesAsync()
    {
        await InitAsync();
        return await _db.Table<SavedVehicle>().OrderByDescending(x => x.SavedDate).ToListAsync();
    }
}
