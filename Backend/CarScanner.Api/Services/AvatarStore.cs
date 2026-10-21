using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

public enum AvatarStatus
{
    /// <summary>Checked and shown to everybody.</summary>
    Approved,
    /// <summary>Could not be checked automatically; stored but not shown until an administrator approves it.</summary>
    Pending
}

public sealed record AvatarRecord(string AccountId, string PublicId, AvatarStatus Status, DateTime UpdatedAt, int Bytes)
{
    /// <summary>Relative URL of the picture (changes with every new upload), or null while it is not shown.</summary>
    public string? Url => Status == AvatarStatus.Approved ? "/api/avatars/" + PublicId : null;
}

/// <summary>
/// Profile pictures shown on the public ranking. One small JPEG per account, stored next to the database in
/// <c>avatars/</c> under an opaque id (the public URL never contains the account id). A picture is shown only after the
/// automatic check approved it; deleting an account removes its picture.
/// </summary>
public sealed class AvatarStore
{
    /// <summary>The server accepts at most this many bytes; the app sends a 256 px JPEG of about 20-60 KB.</summary>
    public const int MaxBytes = 300 * 1024;
    public const int MaxSidePixels = 1024;

    /// <summary>Also executed by the ranking store, which joins this table (both live in the same database).</summary>
    public const string SchemaSql = @"CREATE TABLE IF NOT EXISTS AccountAvatars (
AccountId TEXT PRIMARY KEY, PublicId TEXT NOT NULL, Status TEXT NOT NULL, UpdatedAt TEXT NOT NULL, Bytes INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS IX_AccountAvatars_PublicId ON AccountAvatars(PublicId);";

    private readonly string _connectionString;
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AvatarStore(IHostEnvironment env, IConfiguration config)
    {
        var databasePath = BackendDataPath.ResolveDatabasePath(env, config);
        _connectionString = BackendDataPath.CreateConnectionString(databasePath);
        _directory = Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "avatars");
        Directory.CreateDirectory(_directory);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        command.ExecuteNonQuery();
    }

    /// <summary>True for a JPEG file (start-of-image marker) within the size limit.</summary>
    public static bool IsAcceptableJpeg(ReadOnlySpan<byte> data, out string? reason)
    {
        reason = null;
        if (data.Length < 128)
            reason = "too small";
        else if (data.Length > MaxBytes)
            reason = "too large";
        else if (!(data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF))
            reason = "not a JPEG";
        else if (TryReadJpegSize(data, out var width, out var height) && (width > MaxSidePixels || height > MaxSidePixels))
            reason = "dimensions too large";

        return reason is null;
    }

    /// <summary>Reads the pixel size from the first start-of-frame marker. False when the file has none.</summary>
    public static bool TryReadJpegSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        var i = 2;
        while (i + 8 < data.Length)
        {
            if (data[i] != 0xFF) { i++; continue; }
            var marker = data[i + 1];
            if (marker == 0xFF) { i++; continue; }
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }

            var length = (data[i + 2] << 8) | data[i + 3];
            // Start-of-frame markers (baseline / progressive ...), except DHT 0xC4, JPG 0xC8 and DAC 0xCC.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                height = (data[i + 5] << 8) | data[i + 6];
                width = (data[i + 7] << 8) | data[i + 8];
                return width > 0 && height > 0;
            }

            if (length < 2) return false;
            i += 2 + length;
        }
        return false;
    }

    public async Task<AvatarRecord> SaveAsync(string accountId, byte[] jpeg, AvatarStatus status, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var publicId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            await File.WriteAllBytesAsync(FilePath(publicId), jpeg, ct);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            string? oldId = null;
            await using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT PublicId FROM AccountAvatars WHERE AccountId=$a";
                read.Parameters.AddWithValue("$a", accountId);
                oldId = await read.ExecuteScalarAsync(ct) as string;
            }

            var now = DateTime.UtcNow;
            await using (var write = connection.CreateCommand())
            {
                write.CommandText = @"INSERT INTO AccountAvatars(AccountId,PublicId,Status,UpdatedAt,Bytes) VALUES($a,$p,$s,$u,$b)
ON CONFLICT(AccountId) DO UPDATE SET PublicId=$p, Status=$s, UpdatedAt=$u, Bytes=$b";
                write.Parameters.AddWithValue("$a", accountId);
                write.Parameters.AddWithValue("$p", publicId);
                write.Parameters.AddWithValue("$s", status.ToString());
                write.Parameters.AddWithValue("$u", now.ToString("O"));
                write.Parameters.AddWithValue("$b", jpeg.Length);
                await write.ExecuteNonQueryAsync(ct);
            }

            if (!string.IsNullOrEmpty(oldId))
                TryDeleteFile(oldId);

            return new AvatarRecord(accountId, publicId, status, now, jpeg.Length);
        }
        finally { _gate.Release(); }
    }

    public async Task<AvatarRecord?> GetForAccountAsync(string accountId, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PublicId,Status,UpdatedAt,Bytes FROM AccountAvatars WHERE AccountId=$a";
        command.Parameters.AddWithValue("$a", accountId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new AvatarRecord(
            accountId,
            reader.GetString(0),
            Enum.TryParse<AvatarStatus>(reader.GetString(1), out var status) ? status : AvatarStatus.Pending,
            DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
            reader.GetInt32(3));
    }

    /// <summary>The picture bytes, only while the picture is approved (everything else is treated as not found).</summary>
    public async Task<byte[]?> ReadApprovedAsync(string publicId, CancellationToken ct)
    {
        if (!IsPublicId(publicId))
            return null;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM AccountAvatars WHERE PublicId=$p AND Status='Approved'";
        command.Parameters.AddWithValue("$p", publicId);
        if (await command.ExecuteScalarAsync(ct) is null)
            return null;

        var path = FilePath(publicId);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    /// <summary>Every stored picture for the administrator page: pictures waiting for approval first, then the newest.</summary>
    public async Task<IReadOnlyList<AvatarRecord>> ListAsync(int limit, CancellationToken ct)
    {
        var result = new List<AvatarRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AccountId,PublicId,Status,UpdatedAt,Bytes FROM AccountAvatars ORDER BY CASE Status WHEN 'Pending' THEN 0 ELSE 1 END, UpdatedAt DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(ReadRecord(reader));
        return result;
    }

    public async Task<AvatarRecord?> GetByPublicIdAsync(string publicId, CancellationToken ct)
    {
        if (!IsPublicId(publicId))
            return null;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AccountId,PublicId,Status,UpdatedAt,Bytes FROM AccountAvatars WHERE PublicId=$p";
        command.Parameters.AddWithValue("$p", publicId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRecord(reader) : null;
    }

    /// <summary>The picture bytes whatever its status. Only for the administrator page (to review a picture before approving it).</summary>
    public async Task<byte[]?> ReadAnyAsync(string publicId, CancellationToken ct)
    {
        if (await GetByPublicIdAsync(publicId, ct) is null)
            return null;

        var path = FilePath(publicId);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    /// <summary>Approves or hides the picture of an account. False when the account has no picture.</summary>
    public async Task<bool> SetStatusAsync(string accountId, AvatarStatus status, CancellationToken ct, string? onlyIfPublicId = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        // With onlyIfPublicId the change applies only while that very picture is still the account's current one.
        command.CommandText = onlyIfPublicId is null
            ? "UPDATE AccountAvatars SET Status=$s WHERE AccountId=$a"
            : "UPDATE AccountAvatars SET Status=$s WHERE AccountId=$a AND PublicId=$p";
        command.Parameters.AddWithValue("$s", status.ToString());
        command.Parameters.AddWithValue("$a", accountId);
        if (onlyIfPublicId is not null)
            command.Parameters.AddWithValue("$p", onlyIfPublicId);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>Removes the picture only while <paramref name="publicId"/> is still the account's current one.</summary>
    public async Task DeleteIfCurrentAsync(string accountId, string publicId, CancellationToken ct)
    {
        var current = await GetForAccountAsync(accountId, ct);
        if (current is not null && string.Equals(current.PublicId, publicId, StringComparison.Ordinal))
            await DeleteAsync(accountId, ct);
    }

    private static AvatarRecord ReadRecord(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        Enum.TryParse<AvatarStatus>(reader.GetString(2), out var status) ? status : AvatarStatus.Pending,
        DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
        reader.GetInt32(4));

    /// <summary>Removes the picture of an account (own removal, account deletion, or an administrator).</summary>
    public async Task DeleteAsync(string accountId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            string? publicId;
            await using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT PublicId FROM AccountAvatars WHERE AccountId=$a";
                read.Parameters.AddWithValue("$a", accountId);
                publicId = await read.ExecuteScalarAsync(ct) as string;
            }

            await using (var delete = connection.CreateCommand())
            {
                delete.CommandText = "DELETE FROM AccountAvatars WHERE AccountId=$a";
                delete.Parameters.AddWithValue("$a", accountId);
                await delete.ExecuteNonQueryAsync(ct);
            }

            if (!string.IsNullOrEmpty(publicId))
                TryDeleteFile(publicId);
        }
        finally { _gate.Release(); }
    }

    public static bool IsPublicId(string? value) =>
        value is { Length: 32 } && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private string FilePath(string publicId) => Path.Combine(_directory, publicId + ".jpg");

    private void TryDeleteFile(string publicId)
    {
        try
        {
            if (IsPublicId(publicId))
                File.Delete(FilePath(publicId));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
