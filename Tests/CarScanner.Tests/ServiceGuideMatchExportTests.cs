using System.Text.Json;
using CarScanner.Models;
using CarScanner.Services.ServiceProcedures;

namespace CarScanner.Tests;

/// <summary>
/// Research tooling, not a behaviour test. When <c>OBD360_RESEARCH_EXPORT_DIR</c> names a folder and
/// <c>OBD360_RESEARCH_VEHICLES_JSON</c> names a JSON file of <c>[{"make":"Ford","model":"Mustang","years":[2016,2019]}]</c>, this writes
/// <c>guide-matches.json</c> there: for every vehicle and model year, the guided procedures the Service page would list for that
/// vehicle (the shipped <c>service_procedures.json</c>, matched by the app's own <see cref="ServiceProcedureCatalogView"/>), with the
/// kind, the compatibility status, the method and what the procedure does to the car. research/tools/build-capability-matrix.js
/// reads it. Without the variables the test does nothing and passes. Nothing here talks to a vehicle.
/// </summary>
public sealed class ServiceGuideMatchExportTests
{
    [Fact]
    public void WritesTheGuideMatchesOfTheGivenVehicles_WhenAFolderAndAListAreGiven()
    {
        var folder = Environment.GetEnvironmentVariable("OBD360_RESEARCH_EXPORT_DIR");
        var list = Environment.GetEnvironmentVariable("OBD360_RESEARCH_VEHICLES_JSON");
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(list) || !File.Exists(list))
            return;

        var catalog = ServiceProcedureParser.Parse(File.ReadAllText(CatalogPath())).Procedures;
        var vehicles = JsonDocument.Parse(File.ReadAllText(list)).RootElement;

        Directory.CreateDirectory(folder);
        using var stream = File.Create(Path.Combine(folder, "guide-matches.json"));
        using var json = new Utf8JsonWriter(stream);
        json.WriteStartArray();
        foreach (var vehicle in vehicles.EnumerateArray())
        {
            var make = vehicle.GetProperty("make").GetString() ?? string.Empty;
            var model = vehicle.GetProperty("model").GetString() ?? string.Empty;
            foreach (var yearElement in vehicle.GetProperty("years").EnumerateArray())
            {
                var year = yearElement.GetInt32();
                var facts = ServiceVehicleFacts.From(new VehicleInfo { Manufacturer = make, Model = model, Year = year.ToString() });
                var entries = ServiceProcedureCatalogView.Build(catalog, facts);

                json.WriteStartObject();
                json.WriteString("make", make);
                json.WriteString("model", model);
                json.WriteNumber("year", year);
                json.WriteStartArray("guides");
                foreach (var entry in entries.Where(e => e.Compatibility.Status != ServiceCompatibilityStatus.NotSupported))
                {
                    json.WriteStartObject();
                    json.WriteString("id", entry.Definition.Id);
                    json.WriteString("kind", entry.Kind.ToString());
                    json.WriteString("status", entry.Compatibility.Status.ToString());
                    json.WriteString("method", entry.Compatibility.Method.ToString());
                    json.WriteString("effect", entry.Effect.ToString());
                    json.WriteString("verification", entry.Verification.ToString());
                    json.WriteBoolean("brandWide", entry.Definition.Model == "*");
                    json.WriteString("name", entry.Definition.NameEn);
                    json.WriteString("source", entry.Definition.Source);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }
        }

        json.WriteEndArray();
    }

    private static string CatalogPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Resources", "Raw", "service_procedures.json");
    }
}
