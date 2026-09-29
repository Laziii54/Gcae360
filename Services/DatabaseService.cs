using CarScanner.Database;
using CarScanner.Models;

namespace CarScanner.Services;

public class DatabaseService
{
    private readonly AppDatabase _db;

    public DatabaseService(AppDatabase db)
    {
        _db = db;
    }

    public Task<List<ScanResult>> GetHistoryAsync() =>
        _db.GetScansAsync();

    public Task<List<ScanSession>> GetScanSessionsAsync() =>
        _db.GetScanSessionsAsync();

    public Task SaveScanAsync(ScanResult scan) =>
        _db.SaveScanAsync(scan);

    public Task ClearHistoryAsync() =>
        _db.ClearHistoryAsync();

    public Task DeleteAllVehicleDataAsync() =>
        _db.DeleteAllVehicleDataAsync();

    public Task<string> ExportDataJsonAsync() =>
        _db.ExportDataJsonAsync();
}
