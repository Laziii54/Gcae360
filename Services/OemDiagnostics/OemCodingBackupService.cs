using System.Text.Json;
using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

public sealed class OemCodingBackupService
{
    private const string FolderName = "oem-coding-backups";

    public async Task<string> SaveAsync(OemCodingBackup backup, CancellationToken token = default)
    {
        var directory = Path.Combine(FileSystem.AppDataDirectory, FolderName);
        Directory.CreateDirectory(directory);
        var safeOperation = new string(backup.OperationId.Where(char.IsLetterOrDigit).ToArray());
        var safeVin = new string(backup.Vin.Where(char.IsLetterOrDigit).ToArray());
        var path = Path.Combine(directory,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{safeVin}-{safeOperation}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(backup, new JsonSerializerOptions
        {
            WriteIndented = true
        }), token).ConfigureAwait(false);
        return path;
    }

    public async Task<OemCodingBackup?> GetLatestAsync(string vin, CancellationToken token = default)
    {
        var directory = Path.Combine(FileSystem.AppDataDirectory, FolderName);
        if (!Directory.Exists(directory)) return null;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var backup = JsonSerializer.Deserialize<OemCodingBackup>(
                    await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
                if (backup is not null && backup.Vin.Equals(vin, StringComparison.OrdinalIgnoreCase))
                    return backup;
            }
            catch (JsonException) { }
        }
        return null;
    }
}
