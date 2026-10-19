using System.IO.Compression;
using System.Text.Json;
using CarScanner.Models;
using CarScanner.Services.Mileage;
using CarScanner.Services.OemDiagnostics;
using Microsoft.Data.Sqlite;

namespace CarScanner.Tests;

/// <summary>
/// Research tooling, not a behaviour test. When the environment variable <c>OBD360_RESEARCH_EXPORT_DIR</c> names a folder, this
/// writes <c>classified-signals.json</c> there: every per-vehicle signal of the shipped catalog that the Service page or the
/// Mileage page would treat as service-related or distance-related, labelled by the app's own classifiers (not by a second copy
/// of their rules), plus one row per vehicle with its signal counts. <c>research/tools/build-research-files.js</c> turns that file
/// into the coverage files under <c>research/</c>. Without the variable the test does nothing and passes.
/// The catalog is read exactly as the app reads it: <c>pid_definitions</c>, services 22 and 21, a make that is not blank.
/// Nothing here talks to a vehicle.
///
/// The other tests of the class always run: they hold the classifiers to the real names and units of the shipped catalog, so a
/// catalog refresh that brings in a name or a unit the rules were not written for fails here instead of reaching the pages.
/// </summary>
public sealed class CatalogResearchExportTests : IClassFixture<CatalogResearchExportTests.DecompressedCatalog>
{
    private const string ExportFolderVariable = "OBD360_RESEARCH_EXPORT_DIR";

    /// <summary>One decompressed copy of the shipped catalog (190 MB) for the tests of this class, deleted when they have run.</summary>
    public sealed class DecompressedCatalog : IDisposable
    {
        public string Path { get; } = Decompress();

        public void Dispose() => TryDelete(Path);
    }

    private readonly DecompressedCatalog _catalog;

    public CatalogResearchExportTests(DecompressedCatalog catalog) => _catalog = catalog;

    private readonly record struct CatalogSignal(string Name, string ParameterId, string Unit);

    private List<CatalogSignal> CatalogSignals()
    {
        using var connection = new SqliteConnection($"Data Source={_catalog.Path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(name, ''), COALESCE(parameter_id, ''), COALESCE(unit, ''), COALESCE(format_json, '')
            FROM pid_definitions
            WHERE pid IN ('22', '21') AND make IS NOT NULL AND TRIM(make) <> '';
            """;
        var signals = new List<CatalogSignal>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            signals.Add(new CatalogSignal(reader.GetString(0), reader.GetString(1), FormatUnit(reader.GetString(3)) ?? reader.GetString(2)));
        return signals;
    }

    [Fact]
    public void EveryOilLifeAndOilDegradationSignalOfTheShippedCatalog_IsAPercentage()
    {
        var oil = CatalogSignals()
            .Where(s => ServiceMetricClassifier.Classify(s.Name, s.ParameterId) is ServiceMetricKind.OilLifePercent or ServiceMetricKind.OilDegradationPercent)
            .ToList();

        Assert.NotEmpty(oil);
        Assert.All(oil, signal => Assert.Equal("percent", signal.Unit.ToLowerInvariant()));
    }

    [Fact]
    public void NoDegradationSignalOfTheShippedCatalog_IsTakenForRemainingLife()
    {
        var all = CatalogSignals();

        foreach (var signal in all.Where(s => s.Name.Contains("oil degradation", StringComparison.OrdinalIgnoreCase)))
            Assert.Equal(ServiceMetricKind.OilDegradationPercent, ServiceMetricClassifier.Classify(signal.Name, signal.ParameterId));

        foreach (var signal in all.Where(s => ServiceMetricClassifier.Classify(s.Name, s.ParameterId) == ServiceMetricKind.OilLifePercent))
            Assert.Contains("oil life", (signal.Name + " " + signal.ParameterId).ToLowerInvariant());
    }

    [Fact]
    public void NoSignalOfTheShippedCatalog_IsTakenForAServiceReminderBecauseItIsAboutVoltageOrTheParkingBrake()
    {
        foreach (var signal in CatalogSignals().Where(s => ServiceMetricClassifier.IsServiceRelevantName(s.Name)))
        {
            Assert.DoesNotContain("voltage maintenance", signal.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("parking brake", signal.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WritesTheClassifiedCatalogSignals_WhenAFolderIsGiven()
    {
        var folder = Environment.GetEnvironmentVariable(ExportFolderVariable);
        if (string.IsNullOrWhiteSpace(folder))
            return;

        Directory.CreateDirectory(folder);
        using var connection = new SqliteConnection($"Data Source={_catalog.Path};Mode=ReadOnly;Pooling=False");
        connection.Open();

        using var stream = File.Create(System.IO.Path.Combine(folder, "classified-signals.json"));
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        json.WriteStartObject();
        json.WriteString("generatedUtc", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        json.WriteString("classifiers", "MileageSignalClassifier + ServiceMetricClassifier, as compiled in this test run");

        WriteSources(connection, json);
        WriteVehicles(connection, json);
        var written = WriteSignals(connection, json);
        json.WriteNumber("signalCount", written);
        json.WriteEndObject();
    }

    private static void WriteSources(SqliteConnection connection, Utf8JsonWriter json)
    {
        json.WriteStartArray("sources");
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.name, s.url, s.database_license, s.contents_license, s.revision, COUNT(p.id)
            FROM source_catalogs s
            INNER JOIN pid_definitions p ON p.source_catalog_id = s.id
            WHERE p.pid IN ('22', '21') AND p.make IS NOT NULL AND TRIM(p.make) <> ''
            GROUP BY s.id ORDER BY s.id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            json.WriteStartObject();
            json.WriteNumber("id", reader.GetInt64(0));
            json.WriteString("name", reader.GetString(1));
            json.WriteString("url", reader.IsDBNull(2) ? string.Empty : reader.GetString(2));
            json.WriteString("databaseLicense", reader.IsDBNull(3) ? string.Empty : reader.GetString(3));
            json.WriteString("contentsLicense", reader.IsDBNull(4) ? string.Empty : reader.GetString(4));
            json.WriteString("revision", reader.IsDBNull(5) ? string.Empty : reader.GetString(5));
            json.WriteNumber("rows", reader.GetInt64(6));
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }

    private static void WriteVehicles(SqliteConnection connection, Utf8JsonWriter json)
    {
        json.WriteStartArray("vehicles");
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT make, COALESCE(model, ''), COUNT(*), SUM(CASE WHEN debug <> 0 THEN 1 ELSE 0 END)
            FROM pid_definitions
            WHERE pid IN ('22', '21') AND make IS NOT NULL AND TRIM(make) <> ''
            GROUP BY make, model ORDER BY make, model;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            json.WriteStartObject();
            json.WriteString("make", reader.GetString(0));
            json.WriteString("model", reader.GetString(1));
            json.WriteNumber("rows", reader.GetInt64(2));
            json.WriteNumber("debugRows", reader.GetInt64(3));
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }

    private static int WriteSignals(SqliteConnection connection, Utf8JsonWriter json)
    {
        var count = 0;
        json.WriteStartArray("signals");
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_catalog_id, make, COALESCE(model, ''), COALESCE(header, ''), COALESCE(eax, ''), COALESCE(command_json, ''),
                   COALESCE(parameter_id, ''), COALESCE(name, ''), COALESCE(path, ''), COALESCE(unit, ''), bit_length, bit_offset,
                   COALESCE(format_json, ''), debug, COALESCE(raw_json, '')
            FROM pid_definitions
            WHERE pid IN ('22', '21') AND make IS NOT NULL AND TRIM(make) <> ''
            ORDER BY make, model, header, command_json, name;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(7);
            var parameterId = reader.GetString(6);

            var mileage = MileageSignalClassifier.Classify(name, out var eventKind);
            var service = ServiceMetricClassifier.IsServiceRelevantName(name)
                ? ServiceMetricClassifier.Classify(name, parameterId)
                : (ServiceMetricKind?)null;

            if (mileage == MileageSignalClass.NotMileage && service is null)
                continue;

            ParseCommand(reader.GetString(5), out var serviceId, out var identifier);

            // The unit the app uses is the one in the signal's own format, else the column (as CatalogOdometerProvider does).
            var unit = FormatUnit(reader.GetString(12)) ?? reader.GetString(9);
            var addressable = ObdbDiagnosticAddressResolver.TryResolve(
                reader.GetString(3), reader.GetString(4), reader.GetString(14), out var address, out _);

            json.WriteStartObject();
            json.WriteNumber("source", reader.GetInt64(0));
            json.WriteString("make", reader.GetString(1));
            json.WriteString("model", reader.GetString(2));
            json.WriteString("header", reader.GetString(3));
            json.WriteString("eax", reader.GetString(4));
            json.WriteBoolean("addressable", addressable);
            if (addressable)
            {
                json.WriteString("tx", address.TransmitCanId.ToString("X"));
                if (address.ReceiveCanId is { } receive)
                    json.WriteString("rx", receive.ToString("X"));
            }

            json.WriteString("service", serviceId);
            json.WriteString("identifier", identifier);
            json.WriteString("parameterId", parameterId);
            json.WriteString("name", name);
            json.WriteString("path", reader.GetString(8));
            json.WriteString("unit", unit);
            if (!reader.IsDBNull(10))
                json.WriteNumber("bitLength", reader.GetInt64(10));
            if (!reader.IsDBNull(11))
                json.WriteNumber("bitOffset", reader.GetInt64(11));
            json.WriteBoolean("debug", !reader.IsDBNull(13) && reader.GetInt64(13) != 0);
            if (mileage != MileageSignalClass.NotMileage)
            {
                json.WriteString("mileage", mileage.ToString());
                if (eventKind != MileageEventKind.None)
                    json.WriteString("event", eventKind.ToString());
            }

            if (service is not null)
                json.WriteString("serviceKind", service.Value.ToString());
            json.WriteEndObject();
            count++;
        }
        json.WriteEndArray();
        return count;
    }

    private static string? FormatUnit(string formatJson)
    {
        if (string.IsNullOrWhiteSpace(formatJson))
            return null;
        try
        {
            using var document = JsonDocument.Parse(formatJson);
            return document.RootElement.TryGetProperty("unit", out var unit) && unit.ValueKind == JsonValueKind.String
                ? unit.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // {"22":"054B"} -> service "22", identifier "054B". Anything else is exported as empty strings and counted by the reader.
    private static void ParseCommand(string commandJson, out string service, out string identifier)
    {
        service = string.Empty;
        identifier = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(commandJson);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                service = property.Name;
                identifier = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.ToString();
                return;
            }
        }
        catch (JsonException)
        {
            // an unreadable command is exported without one
        }
    }

    private static string Decompress()
    {
        var compressed = System.IO.Path.Combine(AppContext.BaseDirectory, "DtcCatalog.sqlite.br");
        Assert.True(File.Exists(compressed), $"Shipped catalog not found at {compressed}");

        var target = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"CatalogResearchExport_{Guid.NewGuid():N}.sqlite");
        using var input = File.OpenRead(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = File.Create(target);
        brotli.CopyTo(output);
        return target;
    }

    private static void TryDelete(string path)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
        catch (IOException)
        {
            // a temp file left behind is harmless
        }
    }
}
