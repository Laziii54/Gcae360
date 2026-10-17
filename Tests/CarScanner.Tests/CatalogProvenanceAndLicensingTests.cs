using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CarScanner.Tests;

/// <summary>
/// Opens the actual shipped <c>DtcCatalog.sqlite.br</c> (decompressed once per test run) and checks the
/// things the multi-source import brief asked for: every source is declared with a license, the two
/// quarantined sources (AutoDiag2, PSA) never resurface in the active lookup tables, there is no duplicate
/// import, and the schema version the catalog reports still matches what DtcCatalogService.cs expects.
/// These all use the real bundled database — never a fabricated fixture.
/// </summary>
public sealed class CatalogProvenanceAndLicensingTests
{
    private static readonly Lazy<string> DecompressedPath = new(Decompress);

    private static string Decompress()
    {
        var compressed = Path.Combine(AppContext.BaseDirectory, "DtcCatalog.sqlite.br");
        Assert.True(File.Exists(compressed), $"Shipped catalog not found at {compressed}");

        var target = Path.Combine(Path.GetTempPath(), $"CatalogProvenanceTests_{Guid.NewGuid():N}.sqlite");
        using var input = File.OpenRead(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = File.Create(target);
        brotli.CopyTo(output);
        return target;
    }

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={DecompressedPath.Value};Mode=ReadOnly");
        conn.Open();
        return conn;
    }

    private static readonly (long Id, string Name, string DatabaseLicense, string ContentsLicense)[] ExpectedManifest =
    {
        (1, "Wal33D/dtc-database", "MIT", ""),
        (2, "AutoDiag2/database", "ODbL-1.0", "DbCL-1.0"),
        (3, "commaai/opendbc", "MIT", "MIT"),
        (4, "ludwig-v/arduino-psa-diag", "GPL-3.0", "GPL-3.0"),
        (5, "OBDb/obdb.community", "MIT-project", "aggregated-data-terms-may-vary"),
        (6, "mdabrowski1990/uds-address", "MIT", "MIT"),
        (7, "foerbsnavi/obdex", "CC0-1.0 data", "CC0-1.0 data"),
        (8, "openvehicles/Open-Vehicle-Monitoring-System-3", "mixed/source-code project", "mixed; see per-file annotations"),
        (9, "evrenonur/obd2-elm327-pid-reference", "README claims MIT", "README claims MIT"),
        (10, "mdabrowski1990/uds", "MIT", "MIT"),
        (11, "openxc/uds-c", "BSD", "BSD"),
    };

    [Fact]
    public void EveryBundledSource_IsDeclaredInTheLicensingManifest_WithANonBlankLicense()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, database_license, contents_license FROM source_catalogs ORDER BY id;";
        using var reader = cmd.ExecuteReader();

        var actual = new List<(long Id, string Name, string DatabaseLicense, string ContentsLicense)>();
        while (reader.Read())
        {
            actual.Add((
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3)));
        }

        Assert.Equal(ExpectedManifest.Length, actual.Count);
        foreach (var expected in ExpectedManifest)
        {
            var found = Assert.Single(actual, a => a.Id == expected.Id);
            Assert.Equal(expected.Name, found.Name);
            Assert.Equal(expected.DatabaseLicense, found.DatabaseLicense);
            Assert.False(string.IsNullOrWhiteSpace(found.DatabaseLicense), $"{found.Name} has no recorded database license");
        }
    }

    [Fact]
    public void AutoDiag2AndPsa_RemainQuarantined_OutOfTheActiveLookupTables()
    {
        using var conn = Open();

        long CountWhere(string table, string column, long id)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {column} = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            return (long)cmd.ExecuteScalar()!;
        }

        Assert.Equal(0, CountWhere("dtc_definitions", "source_catalog_id", 2));
        Assert.Equal(0, CountWhere("dtc_definitions", "source_catalog_id", 4));
        Assert.Equal(0, CountWhere("ecu_addresses", "source_catalog_id", 4));

        using var notesCmd = conn.CreateCommand();
        notesCmd.CommandText = "SELECT notes FROM source_catalogs WHERE id IN (2,4);";
        using var reader = notesCmd.ExecuteReader();
        while (reader.Read())
        {
            var notes = reader.GetString(0);
            Assert.Contains("QUARANTINED", notes, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryDtcAndPidRow_ReferencesASourceThatActuallyExists()
    {
        using var conn = Open();

        void AssertAllReferencedSourcesExist(string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"SELECT DISTINCT t.source_catalog_id FROM {table} t " +
                "LEFT JOIN source_catalogs s ON s.id = t.source_catalog_id " +
                "WHERE s.id IS NULL;";
            using var reader = cmd.ExecuteReader();
            Assert.False(reader.Read(), $"{table} has rows pointing at a source_catalog_id with no source_catalogs row");
        }

        AssertAllReferencedSourcesExist("dtc_definitions");
        AssertAllReferencedSourcesExist("pid_definitions");
        AssertAllReferencedSourcesExist("ecu_addresses");
    }

    [Fact]
    public void DtcDefinitions_HasNoDuplicateImport_OfTheSameSourceRecord()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT source_catalog_id, source_record_key, COUNT(*) c FROM dtc_definitions " +
            "GROUP BY source_catalog_id, source_record_key HAVING c > 1 LIMIT 5;";
        using var reader = cmd.ExecuteReader();
        var duplicates = new List<string>();
        while (reader.Read())
            duplicates.Add($"{reader.GetInt64(0)}:{reader.GetString(1)} x{reader.GetInt64(2)}");

        Assert.True(duplicates.Count == 0, "Duplicate (source_catalog_id, source_record_key) rows: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void SchemaVersionInTheCatalog_MatchesWhatDtcCatalogServiceWillAccept()
    {
        var serviceSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory.Split("Tests")[0], "Services", "DtcCatalogService.cs"));
        var match = Regex.Match(serviceSource, @"ExpectedSchemaVersion\s*=\s*""([^""]+)""");
        Assert.True(match.Success, "Could not find ExpectedSchemaVersion in DtcCatalogService.cs");
        var expected = match.Groups[1].Value;

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version' LIMIT 1;";
        var actual = (string)cmd.ExecuteScalar()!;

        Assert.Equal(expected, actual, ignoreCase: true);
    }

    [Fact]
    public void AWellKnownGenericDtc_ResolvesThroughTheShippedCatalog_FromARemainingSafeSource()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT definition_en, source_catalog_id FROM dtc_definitions " +
            "WHERE code = 'P0301' AND scope = 'generic' ORDER BY source_catalog_id;";
        using var reader = cmd.ExecuteReader();

        var found = false;
        while (reader.Read())
        {
            found = true;
            Assert.Equal("Cylinder 1 Misfire Detected", reader.GetString(0));
            // P0301 must still resolve after the AutoDiag2 quarantine, from Wal33D (1) and/or foerbsnavi (7).
            Assert.NotEqual(2, reader.GetInt64(1));
        }

        Assert.True(found, "P0301 did not resolve at all — generic DTC lookup is broken");
    }
}
