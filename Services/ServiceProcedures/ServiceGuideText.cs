using CarScanner.Models;

namespace CarScanner.Services.ServiceProcedures;

/// <summary>
/// The "reset the dashboard reminder" steps shown on the Oil item page. Nothing here talks to the car: it only chooses
/// which source-documented guides of the catalog apply to the connected vehicle and turns them into plain text.
/// </summary>
public static class ServiceGuideText
{
    /// <summary>
    /// A driver often cannot tell which cluster type the car has (buttons or menu), so up to this many applicable guides
    /// are shown, each with its own title, instead of silently showing only the first one.
    /// </summary>
    public const int MaxVariants = 3;

    /// <summary>
    /// The manual oil / maintenance-reminder guides of <paramref name="applicable"/> (the list that
    /// <see cref="ServiceMatching.SelectForVehicle"/> returns: exact model entries first, otherwise the brand-wide
    /// ones, and the universal guide last). Tool-only notes, tyre-pressure and inspection guides are left out. The
    /// universal guide is used only when no brand-specific guide applies.
    /// </summary>
    public static IReadOnlyList<ServiceProcedureDefinition> OilGuides(IEnumerable<ServiceProcedureDefinition> applicable)
    {
        var manual = applicable
            .Where(item => item.ExecutionType == ServiceProcedureExecutionType.Manual && item.Steps.Count > 0)
            .Where(item => !ServiceKindClassifier.RequiresExternalTool(item))
            .Where(item => ServiceKindClassifier.KindOf(item) is ServiceKind.OilService or ServiceKind.MaintenanceReset)
            .ToList();

        var specific = manual.Where(item => item.Brand != "*").ToList();
        var chosen = specific.Count > 0 ? specific : manual;

        // Oil reminders first (stable order inside each group), then the other maintenance resets.
        return chosen
            .Select((item, index) => (item, index))
            .OrderBy(pair => ServiceKindClassifier.KindOf(pair.item) == ServiceKind.OilService ? 0 : 1)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.item)
            .Take(MaxVariants)
            .ToList();
    }

    /// <summary>One guide: its numbered steps. Several guides: each under its own title, separated by a blank line.</summary>
    public static string Format(IReadOnlyList<ServiceProcedureDefinition> guides, bool english)
    {
        if (guides.Count == 0)
            return string.Empty;

        static string Pick(string ka, string en, bool useEnglish) =>
            useEnglish ? (string.IsNullOrWhiteSpace(en) ? ka : en) : (string.IsNullOrWhiteSpace(ka) ? en : ka);

        string Steps(ServiceProcedureDefinition guide) => string.Join(
            Environment.NewLine,
            guide.Steps.OrderBy(step => step.Order).Select(step => $"{step.Order}. {Pick(step.TextKa, step.TextEn, english)}"));

        if (guides.Count == 1)
            return Steps(guides[0]);

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            guides.Select(guide => Pick(guide.NameKa, guide.NameEn, english) + Environment.NewLine + Steps(guide)));
    }
}
