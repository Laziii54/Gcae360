using System.Security.Cryptography;
using System.Text;
using CarScanner.Services.DataCollection;
using CarScanner.Services.OBD;
using Microsoft.Data.Sqlite;

namespace CarScanner.Api.Services;

/// <summary>
/// Anonymous raw ECU answers contributed by users who opted in. There is no VIN, no account link and no
/// IP address here; a contributor is only a salted hash that is used to count independent sources.
///
/// Reliability is a count, never a promise: "single" → "repeated" (2 devices saw the identical answer) →
/// "corroborated" (3+). Nothing becomes "verified" automatically; that value exists only for a manual review.
/// </summary>
public sealed class EcuObservationStore
{
    public const int MaxItemsPerBatch = 100;
    public const int MaxSourcesPerDevice = 5000;

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EcuObservationStore(IHostEnvironment env, IConfiguration config)
        : this(BackendDataPath.CreateConnectionString(BackendDataPath.ResolveDatabasePath(env, config)))
    {
    }

    public EcuObservationStore(string connectionString)
    {
        _connectionString = connectionString;
        using var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS EcuObservations (
Id INTEGER PRIMARY KEY AUTOINCREMENT,
FirstSeen TEXT NOT NULL,
LastSeen TEXT NOT NULL,
Make TEXT NOT NULL,
Model TEXT NOT NULL,
Year TEXT NOT NULL,
Protocol TEXT NOT NULL,
TxCanId INTEGER NOT NULL,
RxCanId INTEGER NOT NULL,
Ext29 INTEGER NOT NULL,
Request TEXT NOT NULL,
Response TEXT NOT NULL,
SourceCount INTEGER NOT NULL DEFAULT 0,
SeenCount INTEGER NOT NULL DEFAULT 0,
Review TEXT NOT NULL DEFAULT 'unreviewed',
UNIQUE(Make,Model,Year,TxCanId,RxCanId,Ext29,Request,Response));
CREATE TABLE IF NOT EXISTS EcuObservationSources (
ObservationId INTEGER NOT NULL,
SourceHash TEXT NOT NULL,
SeenAt TEXT NOT NULL,
PRIMARY KEY(ObservationId, SourceHash));
CREATE INDEX IF NOT EXISTS IX_EcuObservationSources_Source ON EcuObservationSources(SourceHash);
CREATE INDEX IF NOT EXISTS IX_EcuObservations_Request ON EcuObservations(Make,Model,Year,TxCanId,Request);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>The same device hashes differently here than anywhere else, so tables cannot be joined by it.</summary>
    public static string SourceHash(string deviceId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("ecu-observation-source:" + deviceId));
        return Convert.ToHexString(bytes)[..32];
    }

    public static string Reliability(int sourceCount, string review) =>
        review == "verified" ? "verified"
        : sourceCount >= 3 ? "corroborated"
        : sourceCount == 2 ? "repeated"
        : "single";

    /// <summary>Validates one submitted item with the same rules the phone uses; null = reject.</summary>
    public static CollectedExchange? ValidateItem(EcuObservationItem item)
    {
        if (item is null || item.Tx is < 0 or > 0x1FFFFFFF || item.Rx is < 0 or > 0x1FFFFFFF)
            return null;
        if (!item.Ext29 && (item.Tx > 0x7FF || item.Rx > 0x7FF))
            return null;

        // Standard OBD2 records are compatibility summaries (fixed forms only); everything else is a raw module
        // answer and goes through the module sanitiser. A raw payload is never accepted as a summary.
        if (item.Tx == CollectionSanitizer.StandardTx && item.Rx == 0 && !item.Ext29)
            return CollectionSanitizer.ValidateStandardSummary(item.Tx, item.Rx, item.Ext29, item.Request, item.Response);

        return CollectionSanitizer.Sanitize(new ObdRawExchange(item.Tx, item.Rx, item.Ext29, item.Request ?? "", item.Response ?? ""));
    }

    /// <summary>The observation date sent by the phone: a plain UTC date, not in the future, not absurdly old. Null = invalid.</summary>
    public static string? ParseObservedOn(string? value, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(value))
            return now.UtcDateTime.ToString("yyyy-MM-dd");
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            return null;
        if (date > now.UtcDateTime.Date.AddDays(1) || date < new DateTime(2020, 1, 1))
            return null;
        return date.ToString("yyyy-MM-dd");
    }

    /// <summary>What kind of record a stored row is, derived from its request and answer.</summary>
    public static (string Kind, string Status) Describe(string request, string response)
    {
        var summary = response.StartsWith("valid", StringComparison.Ordinal) ||
                      response.StartsWith("unsupported;", StringComparison.Ordinal) ||
                      response.StartsWith("inconclusive;", StringComparison.Ordinal);
        if (!summary)
            return ("module", "raw");

        var kind = request is "03" or "07" or "0A" ? "obd-dtc"
            : request.Length == 4 && Convert.ToInt32(request[2..], 16) % 0x20 == 0 ? "obd-supported"
            : "obd-pid";
        var status = response.Split(';')[0];
        return (kind, status);
    }

    /// <summary>Returns (accepted, rejected).</summary>
    public async Task<(int Accepted, int Rejected)> AddAsync(EcuObservationBatch batch, string deviceId, CancellationToken ct)
    {
        static string Cut(string? v, int max)
        {
            var t = (v ?? string.Empty).Trim();
            return t.Length > max ? t[..max] : t;
        }

        var source = SourceHash(deviceId);
        var accepted = 0;
        var rejected = 0;
        var nowUtc = DateTimeOffset.UtcNow;

        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

            await using (var count = c.CreateCommand())
            {
                count.Transaction = tx;
                count.CommandText = "SELECT COUNT(*) FROM EcuObservationSources WHERE SourceHash=$s";
                count.Parameters.AddWithValue("$s", source);
                if ((long)(await count.ExecuteScalarAsync(ct) ?? 0L) >= MaxSourcesPerDevice)
                    return (0, batch.Groups?.Sum(g => g.Items?.Count ?? 0) ?? 0);
            }

            var budget = MaxItemsPerBatch;
            foreach (var group in batch.Groups ?? new List<EcuObservationGroup>())
            {
                var make = Cut(group.Make, 40).ToUpperInvariant();
                var model = Cut(group.Model, 60).ToUpperInvariant();
                var year = Cut(group.Year, 8);
                var protocol = Cut(group.Protocol, 60);
                var vehicleTextHasVin = CollectionSanitizer.LooksLikeVin(make) || CollectionSanitizer.LooksLikeVin(model) ||
                                        CollectionSanitizer.LooksLikeVin(year) || CollectionSanitizer.LooksLikeVin(protocol);
                foreach (var item in group.Items ?? new List<EcuObservationItem>())
                {
                    if (budget-- <= 0 || make.Length == 0 || model.Length == 0 || vehicleTextHasVin)
                    {
                        rejected++;
                        continue;
                    }

                    var clean = ValidateItem(item);
                    var day = ParseObservedOn(item.ObservedOn, nowUtc);
                    if (clean is null || day is null)
                    {
                        rejected++;
                        continue;
                    }

                    long id;
                    await using (var upsert = c.CreateCommand())
                    {
                        upsert.Transaction = tx;
                        upsert.CommandText = @"INSERT INTO EcuObservations(FirstSeen,LastSeen,Make,Model,Year,Protocol,TxCanId,RxCanId,Ext29,Request,Response)
VALUES($t,$t,$mk,$md,$y,$p,$tx,$rx,$e,$rq,$rs)
ON CONFLICT(Make,Model,Year,TxCanId,RxCanId,Ext29,Request,Response) DO UPDATE SET LastSeen=MAX(LastSeen,$t), FirstSeen=MIN(FirstSeen,$t)
RETURNING Id;";
                        upsert.Parameters.AddWithValue("$t", day);
                        upsert.Parameters.AddWithValue("$mk", make);
                        upsert.Parameters.AddWithValue("$md", model);
                        upsert.Parameters.AddWithValue("$y", year);
                        upsert.Parameters.AddWithValue("$p", protocol);
                        upsert.Parameters.AddWithValue("$tx", clean.TxCanId);
                        upsert.Parameters.AddWithValue("$rx", clean.RxCanId);
                        upsert.Parameters.AddWithValue("$e", clean.Extended29Bit ? 1 : 0);
                        upsert.Parameters.AddWithValue("$rq", clean.Request);
                        upsert.Parameters.AddWithValue("$rs", clean.Response);
                        id = (long)(await upsert.ExecuteScalarAsync(ct) ?? 0L);
                    }

                    await using (var link = c.CreateCommand())
                    {
                        link.Transaction = tx;
                        link.CommandText = "INSERT OR IGNORE INTO EcuObservationSources(ObservationId,SourceHash,SeenAt) VALUES($o,$s,$t)";
                        link.Parameters.AddWithValue("$o", id);
                        link.Parameters.AddWithValue("$s", source);
                        link.Parameters.AddWithValue("$t", day);
                        await link.ExecuteNonQueryAsync(ct);
                    }

                    await using (var recount = c.CreateCommand())
                    {
                        recount.Transaction = tx;
                        recount.CommandText = @"UPDATE EcuObservations SET
SourceCount=(SELECT COUNT(*) FROM EcuObservationSources WHERE ObservationId=$o),
SeenCount=SeenCount+1 WHERE Id=$o";
                        recount.Parameters.AddWithValue("$o", id);
                        await recount.ExecuteNonQueryAsync(ct);
                    }

                    accepted++;
                }
            }

            await tx.CommitAsync(ct);
        }
        finally { _gate.Release(); }

        return (accepted, rejected);
    }

    /// <summary>Consent withdrawn: remove this device's contributions; answers nobody else saw disappear.</summary>
    public async Task<int> DeleteBySourceAsync(string deviceId, CancellationToken ct)
    {
        var source = SourceHash(deviceId);
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
CREATE TEMP TABLE IF NOT EXISTS _mine(Id INTEGER PRIMARY KEY);
DELETE FROM _mine;
INSERT INTO _mine SELECT ObservationId FROM EcuObservationSources WHERE SourceHash=$s;
DELETE FROM EcuObservationSources WHERE SourceHash=$s;
UPDATE EcuObservations SET SourceCount=(SELECT COUNT(*) FROM EcuObservationSources WHERE ObservationId=EcuObservations.Id)
 WHERE Id IN (SELECT Id FROM _mine);
DELETE FROM EcuObservations WHERE Id IN (SELECT Id FROM _mine) AND SourceCount=0 AND Review<>'verified';
SELECT changes();";
            cmd.Parameters.AddWithValue("$s", source);
            var removed = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
            await tx.CommitAsync(ct);
            return removed;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<EcuObservationRow>> LatestAsync(int count, CancellationToken ct)
    {
        var rows = new List<EcuObservationRow>();
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connectionString);
            await c.OpenAsync(ct);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT o.Id,o.FirstSeen,o.LastSeen,o.Make,o.Model,o.Year,o.Protocol,o.TxCanId,o.RxCanId,o.Ext29,o.Request,o.Response,o.SourceCount,o.SeenCount,o.Review,
(SELECT COUNT(DISTINCT Response) FROM EcuObservations x WHERE x.Make=o.Make AND x.Model=o.Model AND x.Year=o.Year AND x.TxCanId=o.TxCanId AND x.Request=o.Request)
FROM EcuObservations o ORDER BY o.SourceCount DESC, o.LastSeen DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$n", Math.Clamp(count, 1, 1000));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var sources = r.GetInt32(12);
                var review = r.GetString(14);
                rows.Add(new EcuObservationRow(
                    r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
                    r.GetInt32(7), r.GetInt32(8), r.GetInt32(9) == 1, r.GetString(10), r.GetString(11),
                    sources, r.GetInt32(13), r.GetInt32(15), Reliability(sources, review), review));
            }
        }
        finally { _gate.Release(); }
        return rows;
    }
}

public sealed record EcuObservationItem(int Tx, int Rx, bool Ext29, string? Request, string? Response, string? ObservedOn = null);

public sealed record EcuObservationGroup(string? Make, string? Model, string? Year, string? Protocol, List<EcuObservationItem>? Items);

public sealed record EcuObservationBatch(string? AppVersion, List<EcuObservationGroup>? Groups);

public sealed record EcuObservationRow(
    long Id, string FirstSeen, string LastSeen, string Make, string Model, string Year, string Protocol,
    int TxCanId, int RxCanId, bool Ext29, string Request, string Response,
    int SourceCount, int SeenCount, int DistinctAnswersForRequest, string Reliability, string Review)
{
    public string Kind => EcuObservationStore.Describe(Request, Response).Kind;

    public string Status => EcuObservationStore.Describe(Request, Response).Status;
}
