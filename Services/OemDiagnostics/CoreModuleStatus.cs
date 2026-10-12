using CarScanner.Models;

namespace CarScanner.Services.OemDiagnostics;

/// <summary>The modules a driver expects to see in a scan: the ones whose state matters even when they say nothing.</summary>
public enum CoreModule
{
    Engine,
    Transmission,
    Abs,
    Srs,
    Body,
    Cluster,
    HybridBattery
}

/// <summary>
/// What a module scan can truthfully say about one core module. "Reached" means the module itself answered the trouble-code
/// request; silence never means "no faults" and never means "faulty module".
/// </summary>
public enum CoreModuleReach
{
    /// <summary>The module was not asked because the car was moving (only engine and transmission are asked then).</summary>
    NotAskedCarMoving,

    /// <summary>The module was not asked: no verified address for this make is known, so nothing was transmitted.</summary>
    NotAskedNoKnownAddress,

    /// <summary>The module was not asked: the connection is not CAN, so addressed module requests are not possible.</summary>
    NotAskedUnsupportedConnection,

    /// <summary>Asked, nothing came back (absent, asleep, on another bus or behind a gateway all look the same).</summary>
    NoReply,

    /// <summary>Asked, only a frame from a different CAN id came back.</summary>
    ForeignReplyOnly,

    /// <summary>Asked, the request itself failed at the adapter or transport.</summary>
    RequestFailed,

    /// <summary>The module answered, but only with a refusal to the trouble-code request.</summary>
    RefusedCodeList,

    /// <summary>The module started a multi-frame answer that did not arrive completely.</summary>
    IncompleteReply,

    /// <summary>The module answered but does not provide a trouble-code list in this protocol.</summary>
    ReachedNoCodeList,

    /// <summary>The module answered with a complete code list that is empty.</summary>
    ReachedNoFaultCodes,

    /// <summary>The module answered with a complete list containing at least one code.</summary>
    ReachedWithFaultCodes
}

public sealed record CoreModuleStatus(
    CoreModule Module,
    CoreModuleReach Reach,
    int FaultCount,
    string AddressText,
    string NameKa,
    string NameEn,
    string TextKa,
    string TextEn)
{
    /// <summary>True only when the module itself answered.</summary>
    public bool Reached => Reach is CoreModuleReach.ReachedNoCodeList or CoreModuleReach.ReachedNoFaultCodes or CoreModuleReach.ReachedWithFaultCodes
        or CoreModuleReach.IncompleteReply or CoreModuleReach.RefusedCodeList;

    /// <summary>True only for a complete, empty code list from the module itself - the one case that may be called "no faults".</summary>
    public bool ConfirmedNoFaults => Reach == CoreModuleReach.ReachedNoFaultCodes;
}

/// <summary>What the scan knew about the connection when it decided which modules to ask.</summary>
public sealed record ModuleScanContext(bool CarMoving, bool CanConnection)
{
    public static ModuleScanContext Unknown { get; } = new(false, true);
}

/// <summary>
/// Turns the raw outcome of a module scan into one honest status per core module. Pure logic: it reads the probes and module
/// reports the scan produced and adds nothing that the car did not say.
/// </summary>
public static class CoreModuleStatusEvaluator
{
    private static readonly (CoreModule Module, string[] Ids, string Ka, string En)[] Modules =
    {
        (CoreModule.Engine, new[] { "ECM" }, "ძრავი", "Engine"),
        (CoreModule.Transmission, new[] { "TCM" }, "გადაცემათა კოლოფი", "Transmission"),
        (CoreModule.Abs, new[] { "ABS" }, "ABS / მუხრუჭები", "ABS / brakes"),
        (CoreModule.Srs, new[] { "SRS" }, "აირბაგი (SRS)", "Airbag (SRS)"),
        (CoreModule.Body, new[] { "BCM" }, "ძარის მოდული (BCM)", "Body module (BCM)"),
        (CoreModule.Cluster, new[] { "IPC" }, "ხელსაწყოების დაფა", "Instrument cluster"),
        (CoreModule.HybridBattery, new[] { "BMS", "HYB" }, "ჰიბრიდის / მაღალი ძაბვის ბატარეა", "Hybrid / high-voltage battery"),
    };

    public static IReadOnlyList<CoreModuleStatus> Evaluate(
        IReadOnlyList<ModuleDtcReport> reports,
        IReadOnlyList<ModuleProbe> probes,
        ModuleScanContext context)
    {
        var list = new List<CoreModuleStatus>();
        foreach (var (module, ids, ka, en) in Modules)
            list.Add(EvaluateOne(module, ids, ka, en, reports, probes, context));
        return list;
    }

    private static CoreModuleStatus EvaluateOne(
        CoreModule module,
        string[] ids,
        string ka,
        string en,
        IReadOnlyList<ModuleDtcReport> reports,
        IReadOnlyList<ModuleProbe> probes,
        ModuleScanContext context)
    {
        bool Matches(string id) => ids.Contains(id, StringComparer.OrdinalIgnoreCase);

        var answered = reports.Where(report => report.Responded && Matches(report.ModuleId)).ToList();
        var asked = probes.Where(probe => Matches(probe.ModuleId)).ToList();

        // 1) The module itself answered: best evidence first (a code list beats an incomplete one beats a refusal).
        if (answered.Count > 0)
        {
            var complete = answered.Where(report => report.CodeListComplete && report.Protocol != "—").ToList();
            if (complete.Count > 0)
            {
                var faults = complete.Sum(report => report.Codes.Count);
                var address = complete[0].AddressText;
                return faults > 0
                    ? Make(module, CoreModuleReach.ReachedWithFaultCodes, faults, address, ka, en,
                        $"პასუხობს · {faults} შეცდომის კოდი", $"answers · {faults} fault code(s)")
                    : Make(module, CoreModuleReach.ReachedNoFaultCodes, 0, address, ka, en,
                        "პასუხობს · შეცდომის კოდი არ არის (სრული სია მიღებულია)", "answers · no fault codes (complete list received)");
            }

            var incomplete = answered.FirstOrDefault(report => !report.CodeListComplete);
            if (incomplete is not null)
                return Make(module, CoreModuleReach.IncompleteReply, incomplete.Codes.Count, incomplete.AddressText, ka, en,
                    "პასუხი არასრულად მოვიდა — „შეცდომა არ არის“ ვერ ითქმის", "the reply arrived incomplete - \"no faults\" cannot be said");

            return Make(module, CoreModuleReach.ReachedNoCodeList, 0, answered[0].AddressText, ka, en,
                "პასუხობს, მაგრამ შეცდომის კოდების სიას არ აჩვენებს", "answers, but gives no trouble-code list");
        }

        // 2) Asked but the module itself did not give a usable list.
        if (asked.Count > 0)
        {
            ModuleProbe? pick(ModuleProbeState state) => asked.FirstOrDefault(probe => probe.State == state);
            if (pick(ModuleProbeState.AnsweredIncomplete) is { } incompleteProbe)
                return Make(module, CoreModuleReach.IncompleteReply, 0, incompleteProbe.AddressText, ka, en,
                    "პასუხი არასრულად მოვიდა — „შეცდომა არ არის“ ვერ ითქმის", "the reply arrived incomplete - \"no faults\" cannot be said");
            if (pick(ModuleProbeState.AnsweredNegativeOnly) is { } refused)
                return Make(module, CoreModuleReach.RefusedCodeList, 0, refused.AddressText, ka, en,
                    "მოდულმა უპასუხა, მაგრამ კოდების სიაზე უარი თქვა", "the module answered but refused the trouble-code request");
            if (pick(ModuleProbeState.Answered) is { } answeredProbe)
                return Make(module, CoreModuleReach.ReachedNoCodeList, 0, answeredProbe.AddressText, ka, en,
                    "პასუხობს, მაგრამ შეცდომის კოდების სიას არ აჩვენებს", "answers, but gives no trouble-code list");
            if (pick(ModuleProbeState.ForeignReplyOnly) is { } foreign)
                return Make(module, CoreModuleReach.ForeignReplyOnly, 0, foreign.AddressText, ka, en,
                    "პასუხი მხოლოდ სხვა CAN ID-დან მოვიდა — ამ მოდულისგან არა", "a reply came only from another CAN id - not from this module");
            if (pick(ModuleProbeState.Error) is { } failed)
                return Make(module, CoreModuleReach.RequestFailed, 0, failed.AddressText, ka, en,
                    "მოთხოვნა ტექნიკური შეცდომით დასრულდა", "the request failed technically");

            var silent = asked[0];
            return Make(module, CoreModuleReach.NoReply, 0, silent.AddressText, ka, en,
                "არ უპასუხა — არ ნიშნავს, რომ მოდული არ არსებობს ან გაუმართავია", "no reply - this does not mean the module is absent or faulty");
        }

        // 3) Never asked: say why nothing was transmitted.
        if (!context.CanConnection)
            return Make(module, CoreModuleReach.NotAskedUnsupportedConnection, 0, string.Empty, ka, en,
                "არ შემოწმებულა — ეს კავშირი მოდულების მისამართულ მოთხოვნებს არ უჭერს მხარს", "not asked - this connection does not support addressed module requests");
        if (context.CarMoving && module is not (CoreModule.Engine or CoreModule.Transmission))
            return Make(module, CoreModuleReach.NotAskedCarMoving, 0, string.Empty, ka, en,
                "არ შემოწმებულა — მანქანა მოძრაობდა", "not asked - the car was moving");
        return Make(module, CoreModuleReach.NotAskedNoKnownAddress, 0, string.Empty, ka, en,
            "არ შემოწმებულა — ამ მარკისთვის დადასტურებული მისამართი არ არის ცნობილი", "not asked - no verified address is known for this make");
    }

    private static CoreModuleStatus Make(CoreModule module, CoreModuleReach reach, int faults, string address, string ka, string en, string textKa, string textEn) =>
        new(module, reach, faults, address, ka, en, textKa, textEn);
}
