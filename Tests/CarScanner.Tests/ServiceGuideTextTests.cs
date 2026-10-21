using CarScanner.Models;
using CarScanner.Services.ServiceProcedures;

namespace CarScanner.Tests;

/// <summary>
/// The dashboard reset guides shown on the Oil item page, and the guides added in catalog 10 (Mercedes-Benz Sprinter,
/// Fiat / Abarth). Unit tests on the catalog data and the chooser: nothing here talks to a car, and none of it says a
/// guide was tried on a real vehicle.
/// </summary>
public sealed class ServiceGuideTextTests
{
    private static readonly IReadOnlyList<ServiceProcedureDefinition> Real = ServiceTestData.Real.Procedures;

    private static IReadOnlyList<ServiceProcedureDefinition> GuidesFor(string make, string model, string year) =>
        ServiceGuideText.OilGuides(ServiceMatching.SelectForVehicle(Real, ServiceTestData.Car(make, model, year)));

    [Fact]
    public void Catalog10_HasTheNewGuides_AndTheCatalogStillLoadsWithoutIssues()
    {
        // Catalog 10 added these guides; the exact version is pinned in ServiceCatalogContentTests, so a later bump
        // does not need to touch this test.
        Assert.True(ServiceTestData.Real.CatalogVersion >= 10);
        Assert.DoesNotContain(ServiceTestData.Real.Issues, issue => issue.Excluded);
        Assert.Contains(Real, p => p.Id == "mercedes-benz.sprinter.2007-2018.service-reset-no-steering-controls");
        Assert.Contains(Real, p => p.Id == "mercedes-benz.sprinter.2007-2018.service-reset-steering-controls");
        Assert.Contains(Real, p => p.Id == "general.fiat.2007-plus.menu-oil-reset");
    }

    [Fact]
    public void EveryNewGuide_HasSequentialSteps_InBothLanguages_AndASource()
    {
        var added = Real.Where(p => p.Id.StartsWith("mercedes-benz.sprinter.", StringComparison.Ordinal) || p.Id == "general.fiat.2007-plus.menu-oil-reset").ToList();
        Assert.Equal(3, added.Count);

        foreach (var guide in added)
        {
            Assert.Equal(ServiceProcedureExecutionType.Manual, guide.ExecutionType);
            Assert.Equal("SourceDocumented", guide.VerificationStatus);   // never "VehicleVerified" without a real-car record
            Assert.False(string.IsNullOrWhiteSpace(guide.Source), guide.Id);
            Assert.True(guide.Steps.Count >= 4, guide.Id);
            Assert.Equal(Enumerable.Range(1, guide.Steps.Count), guide.Steps.Select(s => s.Order));
            Assert.All(guide.Steps, step =>
            {
                Assert.False(string.IsNullOrWhiteSpace(step.TextKa), guide.Id);
                Assert.False(string.IsNullOrWhiteSpace(step.TextEn), guide.Id);
            });
            Assert.False(ServiceKindClassifier.RequiresExternalTool(guide), guide.Id);
            Assert.Equal(ServiceKind.OilService, ServiceKindClassifier.KindOf(guide));
        }
    }

    [Fact]
    public void ASprinter_GetsBothSprinterGuides_NotThePassengerCarMercedesOnes()
    {
        var list = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Mercedes-Benz", "Sprinter 316 CDI", "2014"));
        Assert.Contains(list, e => e.Definition.Id.EndsWith("service-reset-no-steering-controls", StringComparison.Ordinal));
        Assert.Contains(list, e => e.Definition.Id.EndsWith("service-reset-steering-controls", StringComparison.Ordinal));
        Assert.DoesNotContain(list, e => e.Definition.Id.StartsWith("general.mercedes.", StringComparison.Ordinal));

        // A passenger car of the same make is unchanged.
        var car = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Mercedes-Benz", "C 200", "2016"));
        Assert.Contains(car, e => e.Definition.Id.StartsWith("general.mercedes.", StringComparison.Ordinal));
        Assert.DoesNotContain(car, e => e.Definition.Id.StartsWith("mercedes-benz.sprinter.", StringComparison.Ordinal));

        // Outside the documented years the Sprinter guides do not apply.
        var other = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Mercedes-Benz", "Sprinter", "2022"));
        Assert.DoesNotContain(other, e => e.Definition.Id.StartsWith("mercedes-benz.sprinter.", StringComparison.Ordinal));
    }

    [Fact]
    public void AFiat_GetsTheClusterMenuGuide_AsManualOnly()
    {
        var list = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Fiat", "Punto", "2012"));
        var entry = Assert.Single(list, e => e.Definition.Id == "general.fiat.2007-plus.menu-oil-reset");
        Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, entry.Compatibility.Status);
        Assert.Equal(ServiceMethod.Manual, entry.Compatibility.Method);
    }

    [Fact]
    public void TheOilPage_ShowsEveryApplicableVariant_EachWithItsTitle()
    {
        var sprinter = GuidesFor("Mercedes-Benz", "Sprinter", "2014");
        Assert.Equal(2, sprinter.Count);

        var text = ServiceGuideText.Format(sprinter, english: true);
        Assert.Contains("without steering-wheel controls", text);
        Assert.Contains("with steering-wheel controls", text);
        Assert.Contains("1. Switch the ignition on", text);
        Assert.Contains(Environment.NewLine + Environment.NewLine, text);                 // variants are separated by a blank line

        var georgian = ServiceGuideText.Format(sprinter, english: false);
        Assert.Contains("საჭის ღილაკების გარეშე", georgian);
    }

    [Fact]
    public void ABrandWithTwoClusterTypes_ShowsBoth_AndLeavesOutTyreAndInspectionGuides()
    {
        // Toyota 2016: the button cluster guide and the menu cluster guide both apply; the TPMS guide is a different kind.
        var guides = GuidesFor("Toyota", "Corolla", "2016");
        Assert.True(guides.Count >= 2);
        Assert.Contains(guides, g => g.Id == "general.toyota.older.trip-button");
        Assert.Contains(guides, g => g.Id == "general.toyota.newer.mid-settings");
        Assert.DoesNotContain(guides, g => ServiceKindClassifier.KindOf(g) is ServiceKind.TirePressure or ServiceKind.Inspection);
        Assert.True(guides.Count <= ServiceGuideText.MaxVariants);
    }

    [Fact]
    public void TheUniversalGuide_IsUsedOnlyWhenNothingBrandSpecificApplies()
    {
        var unknown = GuidesFor("Zastava", "Unknown", "2005");
        var only = Assert.Single(unknown);
        Assert.Equal("general.any.owners-manual", only.Id);

        var toyota = GuidesFor("Toyota", "Corolla", "2016");
        Assert.DoesNotContain(toyota, g => g.Brand == "*");
    }

    [Fact]
    public void AToolOnlyEntry_IsNeverShownAsAGuide()
    {
        var porsche = GuidesFor("Porsche", "Cayenne", "2012");
        Assert.DoesNotContain(porsche, g => g.Id == "general.porsche.1997-plus.diagnostic-tool-only");
    }

    [Fact]
    public void AKiaEv6_2023_GetsTheServiceIntervalGuide_ButOtherYearsDoNot()
    {
        var list2023 = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Kia", "EV6", "2023"));
        var entry = Assert.Single(list2023, e => e.Definition.Id == "kia.ev6.2023.service-interval-reset");
        Assert.Equal(ServiceCompatibilityStatus.ManualProcedureOnly, entry.Compatibility.Status);
        Assert.Equal(ServiceMethod.Manual, entry.Compatibility.Method);
        Assert.Equal(ServiceKind.MaintenanceReset, ServiceKindClassifier.KindOf(entry.Definition));

        // The source page does not carry this reset path for other model years; the guide is not extended beyond 2023.
        var list2024 = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Kia", "EV6", "2024"));
        Assert.DoesNotContain(list2024, e => e.Definition.Id == "kia.ev6.2023.service-interval-reset");

        // A different Kia model is unaffected.
        var sportage = ServiceProcedureCatalogView.Build(Real, ServiceTestData.Facts("Kia", "Sportage", "2023"));
        Assert.DoesNotContain(sportage, e => e.Definition.Id == "kia.ev6.2023.service-interval-reset");
    }

    [Fact]
    public void SingleGuide_KeepsTheOldPlainNumberedFormat()
    {
        var one = new[] { ServiceTestData.Manual(nameEn: "Oil reminder reset", steps: 2) };
        Assert.Equal("1. step 1" + Environment.NewLine + "2. step 2", ServiceGuideText.Format(one, english: true));
        Assert.Equal(string.Empty, ServiceGuideText.Format(Array.Empty<ServiceProcedureDefinition>(), english: true));
    }
}
