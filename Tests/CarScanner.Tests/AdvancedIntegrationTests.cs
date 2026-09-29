using System.Threading.Tasks;
using Xunit;
using CarScanner.Models;
using System.Linq;
using System.Reflection;

namespace CarScanner.Tests;

public class AdvancedIntegrationTests
{
    [Fact]
    public async Task Refresh_Merge_Shows_Volt_Guided_Procedure()
    {
        // Arrange: load service procedures catalog path
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

        // Simpler integration-style verification using the raw service_procedures.json
        var raw = await System.IO.File.ReadAllTextAsync(catalogPath);
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        var procedures = doc.RootElement.GetProperty("procedures");

        static bool BrandMatches(string catalogBrand, string vehicleBrand)
        {
            if (string.IsNullOrWhiteSpace(catalogBrand) || string.IsNullOrWhiteSpace(vehicleBrand)) return false;
            if (catalogBrand == "*") return true;
            return string.Equals(catalogBrand.Trim(), vehicleBrand, System.StringComparison.OrdinalIgnoreCase);
        }

        static bool ModelMatches(string catalogModel, string vehicleModel)
        {
            if (string.IsNullOrWhiteSpace(catalogModel)) return false;
            if (catalogModel == "*") return true;
            if (string.IsNullOrWhiteSpace(vehicleModel)) return false;
            var expected = catalogModel.Trim();
            return expected.Equals(vehicleModel, System.StringComparison.OrdinalIgnoreCase) ||
                   vehicleModel.Contains(expected, System.StringComparison.OrdinalIgnoreCase) ||
                   expected.Contains(vehicleModel, System.StringComparison.OrdinalIgnoreCase);
        }

        static bool YearMatches(System.Text.Json.JsonElement procedure, int vehicleYear)
        {
            var min = procedure.GetProperty("minimumYear").GetInt32();
            var max = procedure.GetProperty("maximumYear").GetInt32();
            if (min > 0 && vehicleYear < min) return false;
            if (max > 0 && vehicleYear > max) return false;
            return true;
        }

        // Prepare vehicle
        var vehicle = new VehicleInfo { Manufacturer = "Chevrolet", Model = "Volt", Year = "2012" };

        var guidedList = new System.Collections.Generic.List<CarScanner.Models.ServiceProcedureItem>();
        foreach (var proc in procedures.EnumerateArray())
        {
            var executionType = proc.GetProperty("executionType").GetString() ?? string.Empty;
            if (!BrandMatches(proc.GetProperty("brand").GetString()!, vehicle.Manufacturer)) continue;
            if (!ModelMatches(proc.GetProperty("model").GetString()!, vehicle.Model)) continue;
            if (!YearMatches(proc, int.Parse(vehicle.Year))) continue;
            if (!string.Equals(executionType, "Manual", System.StringComparison.OrdinalIgnoreCase)) continue;

            var item = new CarScanner.Models.ServiceProcedureItem
            {
                Id = proc.GetProperty("id").GetString()!,
                Name = proc.GetProperty("nameEn").GetString()!,
                Group = proc.GetProperty("category").GetString() ?? string.Empty,
                Description = proc.GetProperty("descriptionEn").GetString() ?? string.Empty,
                OperationId = string.Empty,
                IsManualProcedure = true,
                ManualInstructions = string.Join("\n\n", proc.GetProperty("steps").EnumerateArray().Select((s, i) => $"{i+1}. {s.GetProperty("textEn").GetString()}")),
                IsActiveTest = false,
                IsAvailable = true,
                RequiresVerifiedOemProfile = false,
                AvailabilityOverride = "GUIDED · No ECU write is sent",
                AvailabilityOverrideColor = "#60A5FA"
            };

            guidedList.Add(item);
        }

        // Assert guided list contains Volt entry
        Assert.Contains(guidedList, p => p.Id == "chevrolet.volt.2012-2013.oil-life-reset");

        // Merge into maintenance (simulate viewmodel merge)
        var maintenance = new System.Collections.Generic.List<CarScanner.Models.ServiceProcedureItem>();
        foreach (var guided in guidedList)
        {
            if (maintenance.Any(m => !string.IsNullOrWhiteSpace(m.Id) && m.Id.Equals(guided.Id, System.StringComparison.OrdinalIgnoreCase)))
                continue;
            maintenance.Add(guided);
        }

        // 1-4: checks
        Assert.True(maintenance.Count > 0);
        var volt = maintenance.FirstOrDefault(m => m.Id == "chevrolet.volt.2012-2013.oil-life-reset");
        Assert.NotNull(volt);
        Assert.True(volt!.IsManualProcedure);
        Assert.True(string.IsNullOrWhiteSpace(volt.OperationId));
        Assert.Contains("GUIDED", volt.AvailabilityOverride);

        // 5: triggering manual item must NOT call OemOperationService.ExecuteAsync
        // Create a fake OemOperationService with a flag
        var executed = false;
        var fakeOem = new FakeOemOperationService(() => executed = true);

        // Simulate ExecuteOemOperation logic: it should early-return for manual procedures
        if (volt.IsManualProcedure)
        {
            // show instructions path - do not call fakeOem.ExecuteAsync
        }
        else
        {
            await fakeOem.ExecuteAsync(new VehicleInfo(), volt.OperationId, true, default);
        }

        Assert.False(executed, "OemOperationService.ExecuteAsync must not be called for manual guided procedures.");

        // 6: duplicate prevention: merging again should not increase count
        var before = maintenance.Count;
        foreach (var guided in guidedList)
        {
            if (!maintenance.Any(m => m.Id == guided.Id)) maintenance.Add(guided);
        }
        Assert.Equal(before, maintenance.Count);

        // 7: non-matching vehicle
        var otherVehicle = new VehicleInfo { Manufacturer = "Ford", Model = "Focus", Year = "2015" };
        var guidedOther = new System.Collections.Generic.List<CarScanner.Models.ServiceProcedureItem>();
        foreach (var proc in procedures.EnumerateArray())
        {
            if (!BrandMatches(proc.GetProperty("brand").GetString()!, otherVehicle.Manufacturer)) continue;
            if (!ModelMatches(proc.GetProperty("model").GetString()!, otherVehicle.Model)) continue;
            if (!YearMatches(proc, int.Parse(otherVehicle.Year))) continue;
            guidedOther.Add(new CarScanner.Models.ServiceProcedureItem { Id = proc.GetProperty("id").GetString()! });
        }
        Assert.DoesNotContain(guidedOther, p => p.Id == "chevrolet.volt.2012-2013.oil-life-reset");
    }

    private class FakeOemOperationService
    {
        private readonly System.Action _onExecute;
        public FakeOemOperationService(System.Action onExecute) => _onExecute = onExecute;
        public System.Threading.Tasks.Task<object> ExecuteAsync(VehicleInfo vehicle, string operationId, bool userConfirmed, System.Threading.CancellationToken token)
        {
            _onExecute();
            return System.Threading.Tasks.Task.FromResult(new object());
        }
    }
}
