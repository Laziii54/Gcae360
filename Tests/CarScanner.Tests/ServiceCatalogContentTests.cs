using CarScanner.Services.ServiceProcedures;

namespace CarScanner.Tests;

public sealed class ServiceCatalogContentTests
{
    private static readonly IReadOnlyList<CarScanner.Models.ServiceProcedureDefinition> Real = ServiceTestData.Real.Procedures;

    [Fact]
    public void Catalog_KeepsEveryRecordOfVersion9_AndIsNowVersion10()
    {
        Assert.Equal(10, ServiceTestData.Real.CatalogVersion);
        Assert.Contains(Real, p => p.Id == "chevrolet.volt.2012-2013.oil-life-reset");
        Assert.Contains(Real, p => p.Id == "general.mercedes.2014-2021.w205-w213");
        Assert.Contains(Real, p => p.Id == "general.toyota.2008-plus.tpms-reset-switch");
        Assert.Contains(Real, p => p.Id == "general.honda.2009-plus.tpms-calibration");
        Assert.Contains(Real, p => p.Id == "general.hyundai.2010-plus.tpms-cluster-menu-reset");
        Assert.Contains(Real, p => p.Id == "general.vwgroup.2004-plus.inspection-reset-menu");
    }

    [Fact]
    public void EveryRecord_HasASource_AStatus_AndNoneClaimsRealVehicleVerification()
    {
        Assert.All(Real, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Source), p.Id);
            Assert.Equal("SourceDocumented", p.VerificationStatus);
            Assert.NotEqual("VehicleVerified", p.VerificationStatus);
        });
    }

    [Fact]
    public void Toyota2015_GetsTheTpmsGuide_AsManualOnly()
    {
        var list = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Toyota", "Corolla", "2015"));
        var tpms = Assert.Single(list, e => e.Kind == ServiceKind.TirePressure);
        Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, tpms.Compatibility.Status);
        Assert.Equal(ServiceMethod.Manual, tpms.Compatibility.Method);
    }

    [Fact]
    public void Toyota2005_IsTooOldForTheTpmsGuide()
    {
        var list = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Toyota", "Corolla", "2005"));
        Assert.DoesNotContain(list, e => e.Kind == ServiceKind.TirePressure);
    }

    [Fact]
    public void Lexus_UsesTheToyotaGuide_ThroughTheSharedBrandList()
    {
        var list = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Lexus", "IS 250", "2012"));
        Assert.Contains(list, e => e.Definition.Id == "general.toyota.2008-plus.tpms-reset-switch");
    }

    [Fact]
    public void Honda_And_Hyundai_GetTheirOwnTpmsGuides()
    {
        Assert.Contains(ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Honda", "Civic", "2016")),
            e => e.Definition.Id == "general.honda.2009-plus.tpms-calibration");
        var hyundai = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Hyundai", "Tucson", "2018"));
        Assert.Contains(hyundai, e => e.Definition.Id == "general.hyundai.2010-plus.tpms-cluster-menu-reset");
        Assert.DoesNotContain(hyundai, e => e.Definition.Id == "general.honda.2009-plus.tpms-calibration");
    }

    [Fact]
    public void VolkswagenGroup_HasASeparateInspectionReset()
    {
        foreach (var brand in new[] { "Volkswagen", "Skoda", "SEAT" })
        {
            var inspection = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts(brand, "Unknown", "2015"), null, ServiceKind.Inspection);
            Assert.Contains(inspection, e => e.Definition.Id == "general.vwgroup.2004-plus.inspection-reset-menu");
        }

        // Audi's own entry says the inspection reset often needs a tool, so it is not claimed for Audi.
        var audi = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Audi", "A4", "2018"), null, ServiceKind.Inspection);
        Assert.DoesNotContain(audi, e => e.Definition.Id == "general.vwgroup.2004-plus.inspection-reset-menu");
    }

    [Fact]
    public void ToolOnlyEntries_AreFlagged_AndNeverOfferedAsGuides()
    {
        var flagged = Real.Where(p => p.ToolRequired).ToList();
        Assert.True(flagged.Count >= 7);

        foreach (var p in flagged)
        {
            var brand = p.Brand.Split('|')[0].Split('/')[0];
            var result = ServiceCompatibilityEvaluator.Evaluate(p, ServiceTestData.Facts(brand, "Unknown", p.MinimumYear > 0 ? p.MinimumYear.ToString() : "2015"));
            Assert.NotEqual(ServiceCompatibilityStatus.ManualProcedureOnly, result.Status);
            Assert.NotEqual(ServiceCompatibilityStatus.Supported, result.Status);
        }
    }

    [Fact]
    public void RealCatalog_ProducesNoSupportedProcedure_WithoutAReviewedAutomaticPath()
    {
        foreach (var facts in new[]
                 {
                     ServiceTestData.Facts("Chevrolet", "Volt", "2013", capabilities: new[] { "can-uds" }),
                     ServiceTestData.Facts("Mercedes-Benz", "C 200", "2016", capabilities: new[] { "can-uds" }),
                     ServiceTestData.Facts("BMW", "320d", "2012", capabilities: new[] { "can-uds" }),
                     ServiceTestData.Facts("Audi", "A4", "2018", capabilities: new[] { "can-uds" }),
                     ServiceTestData.Facts("Volkswagen", "Golf", "2017", capabilities: new[] { "can-uds" })
                 })
        {
            Assert.DoesNotContain(ServiceProcedureCatalogView.Build(Real, facts), e => e.Compatibility.Status == ServiceCompatibilityStatus.Supported);
        }
    }

    [Fact]
    public void RequestedBrands_AllHaveAtLeastOneGuide()
    {
        var brands = new[]
        {
            "Chevrolet", "Ford", "Toyota", "Honda", "Hyundai", "Kia", "Volkswagen", "Audi", "BMW", "Mercedes-Benz",
            "Nissan", "Mazda", "Subaru", "Volvo"
        };

        foreach (var brand in brands)
        {
            var list = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts(brand));
            Assert.True(list.Count > 0, $"{brand} has no guide");
        }
    }
}

public sealed class ServiceProcedureTextTests
{
    [Fact]
    public void EveryEnumValue_HasTextInBothLanguages()
    {
        foreach (var en in new[] { true, false })
        {
            foreach (var kind in Enum.GetValues<ServiceKind>())
                Assert.False(string.IsNullOrWhiteSpace(ServiceProcedureTexts.Kind(kind, en)));
            foreach (var status in Enum.GetValues<ServiceCompatibilityStatus>())
                Assert.False(string.IsNullOrWhiteSpace(ServiceProcedureTexts.Status(status, en)));
            foreach (var method in Enum.GetValues<ServiceMethod>())
            {
                Assert.False(string.IsNullOrWhiteSpace(ServiceProcedureTexts.Method(method, en)));
                Assert.False(string.IsNullOrWhiteSpace(ServiceProcedureTexts.MethodShort(method, en)));
            }

            foreach (var reason in Enum.GetValues<ServiceReason>())
                Assert.False(string.IsNullOrWhiteSpace(ServiceProcedureTexts.Reason(reason, en)));
            foreach (var verification in Enum.GetValues<ServiceVerification>())
                Assert.False(string.IsNullOrWhiteSpace(ServiceProcedureTexts.Verification(verification, en)));
        }
    }

    [Fact]
    public void Texts_AreDistinctPerValue()
    {
        Assert.Equal(Enum.GetValues<ServiceReason>().Length,
            Enum.GetValues<ServiceReason>().Select(r => ServiceProcedureTexts.Reason(r, true)).Distinct().Count());
        Assert.Equal(Enum.GetValues<ServiceCompatibilityStatus>().Length,
            Enum.GetValues<ServiceCompatibilityStatus>().Select(s => ServiceProcedureTexts.Status(s, false)).Distinct().Count());
    }

    [Fact]
    public void ManualVerification_NeverClaimsTheAppChecksTheResult()
    {
        Assert.Contains("dashboard", ServiceProcedureTexts.DashboardVerification(true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot check", ServiceProcedureTexts.ManualPrerequisites(true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirmed only if it really changed", ServiceProcedureTexts.ReadBackVerification(true));
    }

    [Fact]
    public void SupportedColor_IsOnlyUsedForSupported()
    {
        var supported = ServiceProcedureTexts.StatusColor(ServiceCompatibilityStatus.Supported);
        foreach (var status in Enum.GetValues<ServiceCompatibilityStatus>().Where(s => s != ServiceCompatibilityStatus.Supported))
            Assert.NotEqual(supported, ServiceProcedureTexts.StatusColor(status));
    }
}
