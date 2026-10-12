using System.Globalization;
using System.Text;
using CarScanner.Models;

namespace CarScanner.Services.ServiceProcedures;

public enum ServiceKind
{
    OilService,
    MaintenanceReset,
    Inspection,
    TirePressure,
    Battery,
    ParkingBrake,
    Other
}

/// <summary>How a procedure is carried out.</summary>
public enum ServiceMethod
{
    /// <summary>OBD360 performs it through the diagnostic interface (reviewed, read-back verified paths only).</summary>
    Automatic,

    /// <summary>The driver does it with buttons / menus; OBD360 only shows the documented steps.</summary>
    Manual,

    /// <summary>Documented, but needs a workshop / manufacturer tool the app cannot replace.</summary>
    ExternalTool
}

/// <summary>
/// What OBD360 itself sends to the vehicle for a procedure. This is about the app's traffic, not about what the driver
/// does at the dashboard: a manual guide changes the car only through the driver's own button presses.
/// </summary>
public enum ServiceEffect
{
    /// <summary>The app only shows documented steps; it sends nothing to the vehicle for this procedure.</summary>
    ShowsStepsOnly,

    /// <summary>The app would send state-changing diagnostic requests (reset, adaptation, actuator test …). Reviewed paths only.</summary>
    ChangesVehicleState
}

public enum ServiceCompatibilityStatus
{
    Supported,
    ManualProcedureOnly,
    PartiallySupported,
    CompatibilityUnknown,
    NotSupported
}

public enum ServiceVerification
{
    /// <summary>Tested on a real vehicle.</summary>
    VehicleVerified,

    /// <summary>Taken from a named manufacturer / service source; not tested by OBD360 on a real vehicle.</summary>
    SourceDocumented,

    /// <summary>No source recorded.</summary>
    Unverified
}

public enum ServiceReason
{
    ExactModelMatch,
    BrandGenerationMatch,
    UniversalFallback,

    BrandUnknown,
    ModelUnknown,
    YearUnknown,
    EngineUnknown,
    FuelUnknown,

    BrandMismatch,
    ModelMismatch,
    YearOutOfRange,
    EngineMismatch,
    FuelMismatch,

    AdapterCapabilityMissing,
    ExternalToolRequired,
    NoAutomaticPath,
    NoSteps
}

public sealed record ServiceCompatibility(
    ServiceCompatibilityStatus Status,
    ServiceMethod Method,
    IReadOnlyList<ServiceReason> Reasons,
    IReadOnlyList<ServiceReason> Missing);

/// <summary>The vehicle facts OBD360 already has. Anything unknown is null / empty — never guessed.</summary>
public sealed record ServiceVehicleFacts(
    IReadOnlyList<string> Brands,
    string Model,
    int? Year,
    string Engine,
    string Fuel,
    string Protocol,
    IReadOnlySet<string>? AdapterCapabilities)
{
    public bool HasBrand => Brands.Count > 0;

    public static ServiceVehicleFacts From(VehicleInfo? vehicle, string? fallbackBrand = null, IEnumerable<string>? adapterCapabilities = null)
    {
        static string Known(string? value) =>
            string.IsNullOrWhiteSpace(value) || value.Trim().Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? string.Empty : value.Trim();

        return new ServiceVehicleFacts(
            ServiceMatching.BrandKeys(vehicle?.Manufacturer).Concat(ServiceMatching.BrandKeys(fallbackBrand)).Distinct(StringComparer.Ordinal).ToArray(),
            ServiceMatching.NormalizeModel(vehicle?.Model),
            ServiceMatching.ParseYear(vehicle?.Year),
            Known(vehicle?.Engine),
            Known(vehicle?.FuelType),
            Known(vehicle?.Protocol),
            adapterCapabilities is null ? null : new HashSet<string>(adapterCapabilities, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>One catalog procedure evaluated for one vehicle.</summary>
public sealed record ServiceProcedureEntry(
    ServiceProcedureDefinition Definition,
    ServiceKind Kind,
    ServiceCompatibility Compatibility,
    ServiceVerification Verification)
{
    /// <summary>What the app sends to the vehicle for this procedure (see <see cref="ServiceEffect"/>).</summary>
    public ServiceEffect Effect => ServiceKindClassifier.EffectOf(Definition);
}

public static class ServiceKindClassifier
{
    /// <summary>
    /// A procedure changes vehicle state through the app only when it is an OEM diagnostic operation. Every manual
    /// guide and every tool-only note is information: nothing is sent. Whether an OEM operation may actually run on a
    /// given car is a separate decision (<see cref="ServiceCompatibilityEvaluator"/>), never made from the brand alone.
    /// </summary>
    public static ServiceEffect EffectOf(ServiceProcedureDefinition procedure) =>
        procedure.ExecutionType == ServiceProcedureExecutionType.OemDiagnostic
            ? ServiceEffect.ChangesVehicleState
            : ServiceEffect.ShowsStepsOnly;

    private static readonly string[] ToolPhrases =
    {
        "diagnostic tool", "scan tool", "dealer", "service centre", "service center", "workshop", "authorised", "authorized"
    };

    public static bool TryParseKind(string? value, out ServiceKind kind) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out kind) && Enum.IsDefined(kind);

    public static ServiceKind KindOf(ServiceProcedureDefinition procedure)
    {
        if (TryParseKind(procedure.Kind, out var declared))
            return declared;

        var text = $"{procedure.Id} {procedure.NameEn} {procedure.DescriptionEn} {procedure.Category}".ToLowerInvariant();

        if (text.Contains("tpms") || text.Contains("tire pressure") || text.Contains("tyre pressure"))
            return ServiceKind.TirePressure;
        if (text.Contains("battery") && (text.Contains("regist") || text.Contains("adapt") || text.Contains("coding")))
            return ServiceKind.Battery;
        if (text.Contains("parking brake") || text.Contains("epb") || text.Contains("handbrake"))
            return ServiceKind.ParkingBrake;

        var name = $"{procedure.NameEn} {procedure.Id}".ToLowerInvariant();
        if (name.Contains("inspection") && !name.Contains("oil"))
            return ServiceKind.Inspection;
        if (procedure.Category.Equals("Oil Service", StringComparison.OrdinalIgnoreCase) || name.Contains("oil"))
            return ServiceKind.OilService;
        if (text.Contains("service") || text.Contains("maintenance") || text.Contains("reminder") || text.Contains("interval"))
            return ServiceKind.MaintenanceReset;

        return ServiceKind.Other;
    }

    public static ServiceVerification VerificationOf(ServiceProcedureDefinition procedure)
    {
        if (Enum.TryParse<ServiceVerification>(procedure.VerificationStatus?.Trim(), ignoreCase: true, out var declared) &&
            Enum.IsDefined(declared))
            return declared;

        // Never inferred as VehicleVerified: that needs an explicit record of a real-vehicle test.
        return string.IsNullOrWhiteSpace(procedure.Source) ? ServiceVerification.Unverified : ServiceVerification.SourceDocumented;
    }

    public static bool RequiresExternalTool(ServiceProcedureDefinition procedure)
    {
        if (procedure.ToolRequired)
            return true;

        // Entries that honestly say "no owner method — dealer / diagnostic tool" are not guides the driver can follow.
        var text = string.Join(" ", procedure.Steps.Select(step => step.TextEn)).ToLowerInvariant();
        return procedure.Steps.Count <= 2 && ToolPhrases.Any(text.Contains);
    }

    public static ServiceMethod MethodOf(ServiceProcedureDefinition procedure) =>
        procedure.ExecutionType == ServiceProcedureExecutionType.OemDiagnostic && !string.IsNullOrWhiteSpace(procedure.OemOperationId)
            ? ServiceMethod.Automatic
            : RequiresExternalTool(procedure) ? ServiceMethod.ExternalTool : ServiceMethod.Manual;
}

public static class ServiceCompatibilityEvaluator
{
    /// <param name="automaticPathAvailable">
    /// true only when the app really has a reviewed execution path for this procedure on this vehicle
    /// (an OEM operation that passed the existing verified-profile checks). Never inferred from the brand.
    /// </param>
    public static ServiceCompatibility Evaluate(ServiceProcedureDefinition procedure, ServiceVehicleFacts facts, bool automaticPathAvailable = false)
    {
        var reasons = new List<ServiceReason>();
        var missing = new List<ServiceReason>();
        var method = ServiceKindClassifier.MethodOf(procedure);

        ServiceCompatibility NotSupported(ServiceReason why) =>
            new(ServiceCompatibilityStatus.NotSupported, method, new[] { why }, Array.Empty<ServiceReason>());

        // Brand
        if (procedure.Brand == "*")
        {
            reasons.Add(ServiceReason.UniversalFallback);
        }
        else if (!facts.HasBrand)
        {
            missing.Add(ServiceReason.BrandUnknown);
        }
        else if (!ServiceMatching.BrandMatches(procedure.Brand, facts.Brands))
        {
            return NotSupported(ServiceReason.BrandMismatch);
        }

        // Model
        var exactModel = procedure.Model != "*";
        if (exactModel)
        {
            if (string.IsNullOrEmpty(facts.Model))
                missing.Add(ServiceReason.ModelUnknown);
            else if (!ServiceMatching.ModelMatches(procedure.Model, facts.Model))
                return NotSupported(ServiceReason.ModelMismatch);
        }

        // Year
        if (procedure.MinimumYear > 0 || procedure.MaximumYear > 0)
        {
            if (facts.Year is null)
                missing.Add(ServiceReason.YearUnknown);
            else if (!ServiceMatching.YearInRange(procedure, facts.Year))
                return NotSupported(ServiceReason.YearOutOfRange);
        }

        // Powertrain
        if (procedure.FuelTypes.Count > 0)
        {
            if (facts.Fuel.Length == 0)
                missing.Add(ServiceReason.FuelUnknown);
            else if (!procedure.FuelTypes.Any(f => facts.Fuel.Contains(f, StringComparison.OrdinalIgnoreCase)))
                return NotSupported(ServiceReason.FuelMismatch);
        }

        if (procedure.Engines.Count > 0)
        {
            if (facts.Engine.Length == 0)
                missing.Add(ServiceReason.EngineUnknown);
            else if (!procedure.Engines.Any(e => facts.Engine.Contains(e, StringComparison.OrdinalIgnoreCase)))
                return NotSupported(ServiceReason.EngineMismatch);
        }

        if (missing.Contains(ServiceReason.BrandUnknown))
            return new ServiceCompatibility(ServiceCompatibilityStatus.CompatibilityUnknown, method, reasons, missing);

        if (procedure.Brand != "*")
            reasons.Add(exactModel ? ServiceReason.ExactModelMatch : ServiceReason.BrandGenerationMatch);

        if (missing.Count > 0)
            return new ServiceCompatibility(ServiceCompatibilityStatus.PartiallySupported, method, reasons, missing);

        // The vehicle matches. What can OBD360 actually do?
        if (method == ServiceMethod.Automatic)
        {
            var capabilitiesMissing = facts.AdapterCapabilities is not null &&
                                      procedure.RequiredCapabilities.Any(c => !facts.AdapterCapabilities.Contains(c));
            if (capabilitiesMissing)
                reasons.Add(ServiceReason.AdapterCapabilityMissing);
            else if (!automaticPathAvailable)
                reasons.Add(ServiceReason.NoAutomaticPath);
            else
                return new ServiceCompatibility(ServiceCompatibilityStatus.Supported, method, reasons, missing);

            // No usable automatic path: documented steps are the only honest option.
            return procedure.Steps.Count > 0
                ? new ServiceCompatibility(ServiceCompatibilityStatus.ManualProcedureOnly, ServiceMethod.Manual, reasons, missing)
                : new ServiceCompatibility(ServiceCompatibilityStatus.NotSupported, method, reasons, missing);
        }

        if (method == ServiceMethod.ExternalTool)
        {
            reasons.Add(ServiceReason.ExternalToolRequired);
            return new ServiceCompatibility(ServiceCompatibilityStatus.NotSupported, method, reasons, missing);
        }

        if (procedure.Steps.Count == 0)
        {
            reasons.Add(ServiceReason.NoSteps);
            return new ServiceCompatibility(ServiceCompatibilityStatus.NotSupported, method, reasons, missing);
        }

        return new ServiceCompatibility(ServiceCompatibilityStatus.ManualProcedureOnly, method, reasons, missing);
    }
}

public static class ServiceProcedureCatalogView
{
    public static int Rank(ServiceCompatibilityStatus status) => status switch
    {
        ServiceCompatibilityStatus.Supported => 0,
        ServiceCompatibilityStatus.ManualProcedureOnly => 1,
        ServiceCompatibilityStatus.PartiallySupported => 2,
        ServiceCompatibilityStatus.CompatibilityUnknown => 3,
        _ => 4
    };

    /// <summary>
    /// The procedures to show for a vehicle.
    ///  • Brand known: everything that is not excluded by a known fact. Missing facts (model, year, engine) keep an
    ///    entry in the list as "partially supported" instead of hiding it.
    ///  • Exact model entries hide brand-wide entries of the same brand (the existing rule).
    ///  • Brand unknown: nothing is listed until the user searches; a search looks through the whole catalog.
    ///  • A search also shows entries that do not fit this vehicle, clearly marked as not supported.
    /// </summary>
    public static List<ServiceProcedureEntry> Build(
        IEnumerable<ServiceProcedureDefinition> catalog,
        ServiceVehicleFacts facts,
        string? search = null,
        ServiceKind? kind = null,
        Func<ServiceProcedureDefinition, bool>? automaticPathAvailable = null)
    {
        var searching = !string.IsNullOrWhiteSpace(search);
        if (!facts.HasBrand && !searching)
            return new List<ServiceProcedureEntry>();

        var entries = catalog
            .Select(p => new ServiceProcedureEntry(
                p,
                ServiceKindClassifier.KindOf(p),
                ServiceCompatibilityEvaluator.Evaluate(p, facts, automaticPathAvailable?.Invoke(p) ?? false),
                ServiceKindClassifier.VerificationOf(p)))
            .ToList();

        if (!searching)
        {
            entries.RemoveAll(e => e.Compatibility.Status == ServiceCompatibilityStatus.NotSupported &&
                                   e.Compatibility.Reasons.Any(r => r is ServiceReason.BrandMismatch or ServiceReason.ModelMismatch
                                       or ServiceReason.YearOutOfRange or ServiceReason.FuelMismatch or ServiceReason.EngineMismatch));

            var hasExact = entries.Any(e =>
                e.Definition.Model != "*" && e.Definition.Brand != "*" &&
                e.Compatibility.Status != ServiceCompatibilityStatus.PartiallySupported &&
                e.Compatibility.Status != ServiceCompatibilityStatus.NotSupported);
            if (hasExact)
                entries.RemoveAll(e => e.Definition.Model == "*" && e.Definition.Brand != "*");
        }
        else
        {
            var words = Normalize(search!).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            entries.RemoveAll(e =>
            {
                var haystack = Normalize($"{e.Definition.NameEn} {e.Definition.NameKa} {e.Definition.DescriptionEn} {e.Definition.DescriptionKa} " +
                                         $"{e.Definition.Brand} {e.Definition.Model} {e.Definition.SystemName} {e.Definition.Id}");
                return !words.All(haystack.Contains);
            });
        }

        if (kind is { } wanted)
            entries.RemoveAll(e => e.Kind != wanted);

        return entries
            .OrderBy(e => Rank(e.Compatibility.Status))
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.Definition.Model == "*" ? 1 : 0)
            .ThenBy(e => e.Definition.NameEn, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Lower-case, accent-free text for searching (Georgian letters are kept).</summary>
    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
