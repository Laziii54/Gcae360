using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CarScanner.Tests;

/// <summary>
/// Checks a CANDIDATE catalog (research/tools/build-candidate-catalog.js) against what the app reads from the shipped one. When the
/// environment variable <c>OBD360_CANDIDATE_CATALOG</c> names a candidate (<c>DtcCatalog.candidate.sqlite</c> or its <c>.br</c>), every
/// test of this class runs; without it they do nothing and pass, so the normal run never depends on a file outside the repository.
///
/// What is proved: the five readers of <c>pid_definitions</c> keep working unchanged (their SQL runs on the candidate and answers
/// about as many rows as on the shipped file), the layout is the shipped one plus nullable columns, rows of other sources are
/// untouched, the new columns hold what their description says, and the catalog records one OBDb revision with its attribution.
/// Nothing here talks to a vehicle, and nothing replaces the shipped catalog.
/// </summary>
public sealed class CandidateCatalogCompatibilityTests : IDisposable
{
    private const string CandidateVariable = "OBD360_CANDIDATE_CATALOG";

    // OBDb's ecuTypeEnum (OBDb/.schemas signals.json); the candidate must not hold a type outside it.
    private static readonly string[] EcuTypes =
    {
        "abs", "airbag", "allWheelDrive", "battery", "bodyControl", "charger", "cluster", "dcdcConverter", "door", "driverAssistance",
        "engine", "gateway", "hvac", "hybridControl", "infotainment", "keyless", "lighting", "motor", "parkingAssist", "seat",
        "steering", "suspension", "telematics", "tpms", "transmission", "vehicleControl"
    };

    private static readonly string[] LegacyColumns =
    {
        "id", "source_catalog_id", "make", "model", "header", "command_json", "pid", "parameter_id", "name", "path", "scaling", "unit",
        "suggested_metric", "bit_length", "bit_offset", "eax", "format_json", "debug", "raw_json"
    };

    private static readonly string[] NewColumns =
    {
        "source_repo", "source_file", "year_from", "year_to", "years_json", "dbg_years_json", "ecu_type", "command_extra_json"
    };

    private static readonly string[] ReaderFiles =
    {
        "Services/Mileage/CatalogOdometerProvider.cs",
        "Services/OemDiagnostics/ServiceDataReaderService.cs",
        "Services/OemDiagnostics/ServiceDataReaderService.Search.cs",
        "Services/OemDiagnostics/ModuleLiveDataService.cs",
        "Services/Energy/EvEnergyReader.cs"
    };

    private readonly List<string> _temporaryFiles = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in _temporaryFiles)
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    private string? CandidatePath()
    {
        var configured = Environment.GetEnvironmentVariable(CandidateVariable);
        if (string.IsNullOrWhiteSpace(configured))
            return null;

        Assert.True(File.Exists(configured), $"{CandidateVariable} names a file that does not exist: {configured}");
        return Plain(configured);
    }

    /// <summary>The plain SQLite file for a path that may be Brotli-compressed.</summary>
    private string Plain(string path)
    {
        if (!path.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
            return path;

        var target = Path.Combine(Path.GetTempPath(), $"CandidateCatalog_{Guid.NewGuid():N}.sqlite");
        _temporaryFiles.Add(target);
        using var input = File.OpenRead(path);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = File.Create(target);
        brotli.CopyTo(output);
        return target;
    }

    private string ShippedPath() => Plain(Path.Combine(AppContext.BaseDirectory, "DtcCatalog.sqlite.br"));

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        return connection;
    }

    private static T Scalar<T>(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T));
    }

    private static List<(string Name, string Type, bool NotNull)> Columns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        var result = new List<(string, string, bool)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add((reader.GetString(1), reader.GetString(2), reader.GetInt64(3) != 0));
        return result;
    }

    [Fact]
    public void TheLayoutIsTheShippedOnePlusNullableColumns_AndTheSchemaVersionIsUnchanged()
    {
        if (CandidatePath() is not { } candidate)
            return;

        using var shipped = Open(ShippedPath());
        using var next = Open(candidate);

        var before = Columns(shipped, "pid_definitions");
        var after = Columns(next, "pid_definitions");

        Assert.Equal(LegacyColumns, before.Select(c => c.Name));
        Assert.Equal(before, after.Take(before.Count));
        Assert.Equal(NewColumns, after.Skip(before.Count).Select(c => c.Name));
        Assert.All(after.Skip(before.Count), column => Assert.False(column.NotNull, $"{column.Name} must stay nullable so that old rows and old code are unaffected"));

        Assert.Equal(Scalar<string>(shipped, "SELECT value FROM meta WHERE key = 'schema_version'"), Scalar<string>(next, "SELECT value FROM meta WHERE key = 'schema_version'"));
        Assert.Equal("ok", Scalar<string>(next, "PRAGMA quick_check"));
    }

    [Fact]
    public void EverySqlStatementTheAppRunsOnPidDefinitions_StillRunsAndAnswersAboutAsManyRows()
    {
        if (CandidatePath() is not { } candidate)
            return;

        var root = AppContext.BaseDirectory.Split("Tests")[0];
        var statements = new List<(string File, string Sql)>();
        foreach (var file in ReaderFiles)
        {
            var source = File.ReadAllText(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar)));
            foreach (Match match in Regex.Matches(source, "\"\"\"(.*?)\"\"\"", RegexOptions.Singleline))
            {
                if (match.Groups[1].Value.Contains("FROM pid_definitions", StringComparison.OrdinalIgnoreCase))
                    statements.Add((file, match.Groups[1].Value));
            }
        }

        // five readers, five statements (the Service page has two: its candidate loader and its search of oil-related rows)
        Assert.True(statements.Count >= 5, $"expected the readers' SQL to be found, found {statements.Count} statements");

        using var shipped = Open(ShippedPath());
        using var next = Open(candidate);
        foreach (var (file, sql) in statements)
        {
            var oldCount = CountRows(shipped, sql);
            var newCount = CountRows(next, sql);
            Assert.True(oldCount > 0, $"{file}: the statement answered no row on the shipped catalog for the probe make");
            var allowed = Math.Max(50, (int)(oldCount * 0.05));
            Assert.True(Math.Abs(newCount - oldCount) <= allowed, $"{file}: {oldCount} rows on the shipped catalog, {newCount} on the candidate (allowed difference {allowed})");
        }
    }

    // Runs one statement of the app with the probe make (and 0 for the debug flag) and counts the rows it answers.
    private static int CountRows(SqliteConnection connection, string sql)
    {
        var index = 0;
        var parameters = new List<(string Name, object Value)>();
        var text = Regex.Replace(sql, @"\?", match =>
        {
            index++;
            var before = sql[..match.Index];
            var isDebugFlag = before.TrimEnd().EndsWith("debug =", StringComparison.OrdinalIgnoreCase);
            parameters.Add(($"$p{index}", isDebugFlag ? 0L : "Toyota"));
            return $"$p{index}";
        });

        using var command = connection.CreateCommand();
        command.CommandText = text;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        var rows = 0;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows++;
        return rows;
    }

    [Fact]
    public void RowsOfOtherSourcesAndEveryOtherTable_AreUntouched()
    {
        if (CandidatePath() is not { } candidate)
            return;

        using var shipped = Open(ShippedPath());
        using var next = Open(candidate);

        // source 7 (OBDex, Mode 01) in pid_definitions: byte-identical, ids included
        const string legacy = "id, source_catalog_id, make, model, header, command_json, pid, parameter_id, name, path, scaling, unit, suggested_metric, bit_length, bit_offset, eax, format_json, debug, raw_json";
        string Fingerprint(SqliteConnection c)
        {
            using var command = c.CreateCommand();
            command.CommandText = $"SELECT {legacy} FROM pid_definitions WHERE source_catalog_id <> 5 ORDER BY id";
            using var reader = command.ExecuteReader();
            var builder = new System.Text.StringBuilder();
            while (reader.Read())
            {
                for (var i = 0; i < reader.FieldCount; i++)
                    builder.Append(reader.IsDBNull(i) ? "\u0000" : Convert.ToString(reader.GetValue(i))).Append('\u0001');
                builder.Append('\n');
            }

            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(builder.ToString())));
        }

        Assert.Equal(Fingerprint(shipped), Fingerprint(next));

        // every table the OBDb refresh does not own keeps its row count
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pid_definitions", "vehicle_generations", "vehicle_pid_support", "diagnostic_commands", "source_snapshots", "documentation", "meta",
            "source_catalogs", "obdb_snapshot_repos", "obdb_ecu_entries", "sqlite_sequence"
        };
        using var tables = shipped.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%'";
        var names = new List<string>();
        using (var reader = tables.ExecuteReader())
        {
            while (reader.Read())
                names.Add(reader.GetString(0));
        }

        foreach (var table in names.Where(name => !owned.Contains(name)))
            Assert.Equal(Scalar<long>(shipped, $"SELECT COUNT(*) FROM {table}"), Scalar<long>(next, $"SELECT COUNT(*) FROM {table}"));

        // the eleven declared sources, their names and licences, are unchanged (the manifest test of the shipped catalog keeps passing)
        string Sources(SqliteConnection c) => string.Join("|", ReadAll(c, "SELECT id, name, database_license, contents_license FROM source_catalogs ORDER BY id"));
        Assert.Equal(Sources(shipped), Sources(next));
    }

    private static IEnumerable<string> ReadAll(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            yield return string.Join(",", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i))));
    }

    [Fact]
    public void TheNewColumnsHoldWhatTheyAreDescribedToHold()
    {
        if (CandidatePath() is not { } candidate)
            return;

        using var next = Open(candidate);

        Assert.Equal(0, Scalar<long>(next, "SELECT COUNT(*) FROM pid_definitions WHERE source_catalog_id = 5 AND (source_repo IS NULL OR source_file IS NULL)"));
        Assert.Equal(0, Scalar<long>(next, "SELECT COUNT(*) FROM pid_definitions WHERE source_catalog_id <> 5 AND (source_repo IS NOT NULL OR year_from IS NOT NULL OR year_to IS NOT NULL OR years_json IS NOT NULL OR dbg_years_json IS NOT NULL OR ecu_type IS NOT NULL OR command_extra_json IS NOT NULL)"));
        Assert.Equal(0, Scalar<long>(next, "SELECT COUNT(*) FROM pid_definitions WHERE year_from IS NOT NULL AND year_to IS NOT NULL AND year_from > year_to"));
        Assert.Equal(0, Scalar<long>(next, "SELECT COUNT(*) FROM pid_definitions WHERE years_json = '[]'"));
        Assert.Equal(0, Scalar<long>(next, "SELECT COUNT(*) FROM pid_definitions WHERE dbg_years_json IS NOT NULL AND debug <> 0"));
        Assert.Equal(0, Scalar<long>(next, "SELECT COUNT(*) FROM pid_definitions WHERE source_catalog_id = 5 AND source_repo <> make || '-' || model"));

        foreach (var type in ReadAll(next, "SELECT DISTINCT ecu_type FROM pid_definitions WHERE ecu_type IS NOT NULL"))
            Assert.Contains(type, EcuTypes);

        foreach (var column in new[] { "years_json", "dbg_years_json" })
        {
            foreach (var json in ReadAll(next, $"SELECT DISTINCT {column} FROM pid_definitions WHERE {column} IS NOT NULL"))
            {
                using var document = JsonDocument.Parse(json);
                Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
                foreach (var pair in document.RootElement.EnumerateArray())
                {
                    Assert.Equal(2, pair.GetArrayLength());
                    var from = pair[0];
                    var to = pair[1];
                    Assert.All(new[] { from, to }, end => Assert.True(end.ValueKind is JsonValueKind.Null or JsonValueKind.Number));
                    if (from.ValueKind == JsonValueKind.Number && to.ValueKind == JsonValueKind.Number)
                        Assert.True(from.GetInt32() <= to.GetInt32());
                }
            }
        }

        foreach (var json in ReadAll(next, "SELECT DISTINCT command_extra_json FROM pid_definitions WHERE command_extra_json IS NOT NULL"))
        {
            using var document = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }

        // the hull rule: year_from / year_to never hide a year that years_json allows
        foreach (var row in ReadAll(next, "SELECT DISTINCT year_from, year_to, years_json FROM pid_definitions WHERE years_json IS NOT NULL"))
        {
            var parts = row.Split(',', 3);
            using var document = JsonDocument.Parse(parts[2]);
            var intervals = document.RootElement.EnumerateArray().ToList();
            var first = intervals[0][0];
            var last = intervals[^1][1];
            Assert.Equal(first.ValueKind == JsonValueKind.Null ? "" : first.GetInt32().ToString(), parts[0]);
            Assert.Equal(last.ValueKind == JsonValueKind.Null ? "" : last.GetInt32().ToString(), parts[1]);
        }
    }

    [Fact]
    public void TheCatalogRecordsOneObdbRevision_WithItsAttribution()
    {
        if (CandidatePath() is not { } candidate)
            return;

        using var next = Open(candidate);

        var revision = Scalar<string>(next, "SELECT revision FROM source_catalogs WHERE id = 5");
        Assert.Equal(revision, Scalar<string>(next, "SELECT value FROM meta WHERE key = 'obdb_pid_revision'"));
        Assert.Contains(Scalar<string>(next, "SELECT value FROM meta WHERE key = 'obdb_snapshot_id'"), revision, StringComparison.Ordinal);
        Assert.Equal(Scalar<string>(next, "SELECT value FROM meta WHERE key = 'obdb_snapshot_manifest_sha256'"), Scalar<string>(next, "SELECT sha256 FROM source_catalogs WHERE id = 5"));
        Assert.Equal(Scalar<long>(next, "SELECT CAST(value AS INTEGER) FROM meta WHERE key = 'obdb_snapshot_repositories'"), Scalar<long>(next, "SELECT COUNT(*) FROM obdb_snapshot_repos"));
        Assert.Equal(Scalar<long>(next, "SELECT CAST(value AS INTEGER) FROM meta WHERE key = 'obdb_pid_records_added'"), Scalar<long>(next, "SELECT COUNT(*) FROM pid_definitions WHERE source_catalog_id = 5"));

        // every row points at a repository of the snapshot, and every repository of the snapshot is CC BY-SA 4.0 with a 40-hex commit
        Assert.Equal(0, Scalar<long>(next, "SELECT COUNT(*) FROM pid_definitions p LEFT JOIN obdb_snapshot_repos r ON r.repo = p.source_repo WHERE p.source_catalog_id = 5 AND r.repo IS NULL"));
        Assert.Equal(0, Scalar<long>(next, "SELECT COUNT(*) FROM obdb_snapshot_repos WHERE license <> 'CC-BY-SA-4.0'"));
        Assert.All(ReadAll(next, "SELECT commit_sha FROM obdb_snapshot_repos"), sha => Assert.Matches("^[0-9a-f]{40}$", sha));

        // the other OBDb-derived tables are on the same snapshot: the previous import time is not what source_snapshots ends with
        Assert.Equal(revision.Split(';')[0], Scalar<string>(next, "SELECT archive_name FROM source_snapshots WHERE source_catalog_id = 5 ORDER BY id DESC LIMIT 1"));

        var documentation = Scalar<string>(next, "SELECT content FROM documentation WHERE name = 'OBDB_IMPORT_NOTES'");
        Assert.Contains("Attribution-ShareAlike 4.0", documentation, StringComparison.Ordinal);
        Assert.Contains("OBDb contributors", documentation, StringComparison.Ordinal);
        Assert.Contains("Changes made", documentation, StringComparison.Ordinal);
        var notes = Scalar<string>(next, "SELECT notes FROM source_catalogs WHERE id = 5");
        Assert.Contains("CC-BY-SA-4.0", notes, StringComparison.Ordinal);
        Assert.Contains("CC BY-SA 4.0", notes, StringComparison.Ordinal);
    }
}
