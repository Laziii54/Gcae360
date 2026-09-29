using System.Threading.Tasks;
using System.Text.Json;
using CarScanner.Models;
using Xunit;

namespace CarScanner.Tests;

public class ServiceProcedureTests
{
    [Fact]
    public async Task ServiceProcedureCatalog_Returns_Volt_OilLife()
    {
        // Load the raw JSON catalog from the repository by walking up from the test
        // output directory to find Resources/Raw/service_procedures.json.
        string FindCatalogPath()
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                var candidate = System.IO.Path.Combine(dir, "Resources", "Raw", "service_procedures.json");
                if (System.IO.File.Exists(candidate))
                    return candidate;
                dir = System.IO.Path.GetDirectoryName(dir) ?? dir;
            }
            throw new System.IO.FileNotFoundException("service_procedures.json not found in repository");
        }

        var catalogPath = FindCatalogPath();
        var raw = await System.IO.File.ReadAllTextAsync(catalogPath);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var procedures = root.GetProperty("procedures");

        var found = false;
        foreach (var proc in procedures.EnumerateArray())
        {
            if (proc.GetProperty("id").GetString() == "chevrolet.volt.2012-2013.oil-life-reset")
            {
                // basic sanity
                Assert.Equal("Chevrolet", proc.GetProperty("brand").GetString());
                Assert.Equal("Volt", proc.GetProperty("model").GetString());
                Assert.Equal(2012, proc.GetProperty("minimumYear").GetInt32());
                Assert.Equal(2013, proc.GetProperty("maximumYear").GetInt32());
                Assert.Equal("Manual", proc.GetProperty("executionType").GetString());
                found = true;
                break;
            }
        }


        Assert.True(found, "service_procedures.json must contain the Volt manual oil-life entry.");

        // Now simulate catalog GetForVehicle matching rules (brand/model/year)
        bool BrandMatches(string catalogBrand, string vehicleBrand)
        {
            if (string.IsNullOrWhiteSpace(catalogBrand) || string.IsNullOrWhiteSpace(vehicleBrand))
                return false;
            if (catalogBrand == "*") return true;
            return string.Equals(catalogBrand.Trim(), vehicleBrand, System.StringComparison.OrdinalIgnoreCase);
        }

        bool ModelMatches(string catalogModel, string vehicleModel)
        {
            if (string.IsNullOrWhiteSpace(catalogModel)) return false;
            if (catalogModel == "*") return true;
            if (string.IsNullOrWhiteSpace(vehicleModel)) return false;
            var expected = catalogModel.Trim();
            return expected.Equals(vehicleModel, System.StringComparison.OrdinalIgnoreCase) ||
                   vehicleModel.Contains(expected, System.StringComparison.OrdinalIgnoreCase) ||
                   expected.Contains(vehicleModel, System.StringComparison.OrdinalIgnoreCase);
        }

        bool YearMatches(JsonElement procedure, int vehicleYear)
        {
            var min = procedure.GetProperty("minimumYear").GetInt32();
            var max = procedure.GetProperty("maximumYear").GetInt32();
            if (min > 0 && vehicleYear < min) return false;
            if (max > 0 && vehicleYear > max) return false;
            return true;
        }

        var matches = new System.Collections.Generic.List<JsonElement>();
        foreach (var proc in procedures.EnumerateArray())
        {
            if (BrandMatches(proc.GetProperty("brand").GetString()!, "Chevrolet") &&
                ModelMatches(proc.GetProperty("model").GetString()!, "Volt") &&
                YearMatches(proc, 2012))
            {
                matches.Add(proc);
            }
        }

        Assert.Contains(matches, p => p.GetProperty("id").GetString() == "chevrolet.volt.2012-2013.oil-life-reset");

        // Negative case: different vehicle should not match
        var nonmatches = new System.Collections.Generic.List<JsonElement>();
        foreach (var proc in procedures.EnumerateArray())
        {
            if (BrandMatches(proc.GetProperty("brand").GetString()!, "Ford") &&
                ModelMatches(proc.GetProperty("model").GetString()!, "Focus") &&
                YearMatches(proc, 2015))
            {
                nonmatches.Add(proc);
            }
        }

        Assert.DoesNotContain(nonmatches, p => p.GetProperty("id").GetString() == "chevrolet.volt.2012-2013.oil-life-reset");
    }
}
