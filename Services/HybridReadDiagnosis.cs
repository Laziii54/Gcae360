using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Services;

/// <summary>
/// Why a battery read ended without a usable pack, when the evidence the code already collects decides it. <see cref="None"/>
/// means it does not decide, and the page then keeps the wording it had before. Nothing here sends anything, and nothing infers the
/// state of the car (for example "not READY") from a request that failed.
/// </summary>
public enum HybridFailureKind
{
    /// <summary>Not decided (or nothing failed): the existing wording stays.</summary>
    None,

    /// <summary>The adapter refused a setup command the battery session needs (for example ATS0): a limit of this adapter.</summary>
    SetupNotAccepted,

    /// <summary>Every request ended without an answer from the module (no reply in time, or NO DATA).</summary>
    ModuleSilent,

    /// <summary>Frames came back, but only from another module than the battery module that was asked.</summary>
    OtherModuleAnswered,

    /// <summary>Something came back that could not be read: damaged, incomplete, not the answer to the request, or a value that is not a plausible reading.</summary>
    UnreadableReply,

    /// <summary>The module answered, but fewer cells than the minimum returned a valid value.</summary>
    TooFewCells,

    /// <summary>The link to the adapter was down when the read ended, or a request failed because it dropped.</summary>
    TransportLost
}

/// <param name="Command">The adapter command that was refused (<see cref="HybridFailureKind.SetupNotAccepted"/>).</param>
/// <param name="Count">The number of valid cells that came back (<see cref="HybridFailureKind.TooFewCells"/>).</param>
public sealed record HybridFailure(HybridFailureKind Kind, string? Command = null, int? Count = null);

/// <summary>
/// What the requests of one battery read showed. It is filled while the read runs, from replies and exceptions the reader already
/// receives; it never changes what is sent or how a reply is parsed.
/// </summary>
public sealed class HybridReadEvidence
{
    public int Requests { get; private set; }

    /// <summary>Requests that returned data the pack parser accepted.</summary>
    public int Answered { get; private set; }

    /// <summary>Requests that ended without any answer from the module: no reply in time, or NO DATA.</summary>
    public int Silent { get; private set; }

    /// <summary>Requests whose frames all came from another module.</summary>
    public int OtherModule { get; private set; }

    /// <summary>Requests with text that could not be read: incomplete, not the answer to this request, or not parsed by the pack parser.</summary>
    public int Unreadable { get; private set; }

    /// <summary>Requests the module answered with a negative response.</summary>
    public int Refused { get; private set; }

    /// <summary>Requests that failed in a way none of the other counts describes (adapter status text, bus error, other errors).</summary>
    public int Other { get; private set; }

    /// <summary>Requests that failed because the link to the adapter was down.</summary>
    public int LinkLost { get; private set; }

    /// <summary>Answers that were read but whose value is not a plausible reading.</summary>
    public int ImplausibleValues { get; private set; }

    /// <summary>The adapter command that was refused, if one was.</summary>
    public string? SetupCommand { get; private set; }

    /// <summary>Valid cell readings, as counted by the reader.</summary>
    public int ValidCells { get; set; }

    public void RecordReply(string? raw, HvRequest request, byte[]? data)
    {
        Requests++;
        if (data is not null)
        {
            Answered++;
            return;
        }

        var payload = request.Payload;
        int? did = payload.Length >= 3 ? (payload[1] << 8) | payload[2] : payload.Length == 2 ? payload[1] : null;
        switch (AdapterReadFailure.ClassifyReply(raw, request.Rx, did, payload.Length > 0 ? payload[0] : (byte)0x22).Kind)
        {
            case AdapterFailureKind.NoReplyInTime:
            case AdapterFailureKind.EcuDidNotAnswer:
                Silent++;
                break;
            case AdapterFailureKind.ReplyFromAnotherModule:
                OtherModule++;
                break;
            // "None" here means the tolerant analysis found the answer, but the pack parser could not read the text.
            case AdapterFailureKind.None:
            case AdapterFailureKind.UnexpectedReply:
            case AdapterFailureKind.IncompleteReply:
                Unreadable++;
                break;
            case AdapterFailureKind.EcuRefused:
                Refused++;
                break;
            default:
                Other++;
                break;
        }
    }

    public void RecordException(Exception exception)
    {
        Requests++;
        if (exception is Elm327CommandException { UnsupportedCommand: { Length: > 0 } command })
        {
            SetupCommand ??= command;
            Other++;
            return;
        }

        switch (AdapterReadFailure.Classify(exception).Kind)
        {
            case AdapterFailureKind.NoReplyInTime:
                Silent++;
                break;
            case AdapterFailureKind.AdapterLinkLost:
                LinkLost++;
                break;
            default:
                Other++;
                break;
        }
    }

    /// <summary>A reply was read, but its value is outside what a cell can show.</summary>
    public void RecordImplausibleValue() => ImplausibleValues++;
}

/// <summary>
/// Turns the evidence of a battery read into one finding and one sentence for the page. Pure: no request is sent and no state of the
/// car is assumed. Where the evidence does not decide, <see cref="HybridFailureKind.None"/> keeps the wording the page always had.
/// </summary>
public static class HybridReadDiagnosis
{
    /// <summary>The sentence the page showed for a Volt / Bolt whose battery module gave no pack, before the causes were told apart.</summary>
    public const string FallbackStatus = "ბატარეამ არ უპასუხა. ჩართე მანქანა (READY) და სცადე თავიდან.";

    /// <summary>
    /// What an exception thrown out of a battery read says, when it says something specific: a refused setup command, or a lost
    /// link. Cancellation and every other exception are not classified here and keep propagating as they did.
    /// </summary>
    public static HybridFailure? FromException(Exception exception)
    {
        if (exception is OperationCanceledException)
            return null;

        if (exception is Elm327CommandException { UnsupportedCommand: { Length: > 0 } command })
            return new HybridFailure(HybridFailureKind.SetupNotAccepted, command);

        return AdapterReadFailure.Classify(exception).Kind == AdapterFailureKind.AdapterLinkLost
            ? new HybridFailure(HybridFailureKind.TransportLost)
            : null;
    }

    /// <summary>
    /// The finding for a read that returned no pack. <paramref name="transportDown"/> is the state of the link when the read ended.
    /// </summary>
    public static HybridFailure Decide(HybridReadEvidence evidence, int minimumCells, bool transportDown)
    {
        if (evidence.SetupCommand is { } command)
            return new HybridFailure(HybridFailureKind.SetupNotAccepted, command);

        if (transportDown)
            return new HybridFailure(HybridFailureKind.TransportLost);

        if (evidence.ValidCells >= minimumCells)
            return new HybridFailure(HybridFailureKind.None);

        if (evidence.ValidCells > 0)
            return new HybridFailure(HybridFailureKind.TooFewCells, Count: evidence.ValidCells);

        if (evidence.LinkLost > 0)
            return new HybridFailure(HybridFailureKind.TransportLost);

        if (evidence.OtherModule > 0)
            return new HybridFailure(HybridFailureKind.OtherModuleAnswered);

        if (evidence.Unreadable > 0 || evidence.ImplausibleValues > 0)
            return new HybridFailure(HybridFailureKind.UnreadableReply);

        // Silence is only reported when nothing else was seen: a refusal, a bus error or a mixture is not "did not respond".
        if (evidence.Silent > 0 && evidence.Silent == evidence.Requests)
            return new HybridFailure(HybridFailureKind.ModuleSilent);

        return new HybridFailure(HybridFailureKind.None);
    }

    /// <summary>The Georgian source text for the page (the display translation turns it into the language chosen).</summary>
    public static string StatusText(HybridFailure failure) => failure.Kind switch
    {
        HybridFailureKind.SetupNotAccepted =>
            $"ადაპტერი არ იღებს ბატარეის შემოწმებისთვის საჭირო პარამეტრს ({failure.Command}). სცადე სხვა ადაპტერი.",
        HybridFailureKind.ModuleSilent =>
            "ბატარეის მოდულმა მოთხოვნებს არ უპასუხა. მიზეზი რამდენიმე შეიძლება იყოს — მაგალითად, მანქანა ჩართული (READY) არ არის ან ადაპტერს ამ მოდულთან წვდომა არ აქვს. შეამოწმე მანქანის მდგომარეობა და სცადე თავიდან.",
        HybridFailureKind.OtherModuleAnswered =>
            "ადაპტერმა პასუხი სხვა მოდულიდან დააბრუნა და არა ბატარეის მოდულიდან. შესაძლოა ადაპტერი კონკრეტულ მოდულთან მიმართვას ვერ უჭერს მხარს.",
        HybridFailureKind.UnreadableReply =>
            "ბატარეის მოდულიდან პასუხი მოვიდა, მაგრამ მისი წაკითხვა ვერ მოხერხდა (დაზიანებული ან მოულოდნელი ფორმატი). სცადე თავიდან; თუ განმეორდა, გამომიგზავნე კავშირის ლოგი „ადაპტერის“ გვერდიდან.",
        HybridFailureKind.TooFewCells =>
            $"ბატარეის {GmVoltBoltBattery.CellCount} უჯრედიდან მხოლოდ {failure.Count}-მა დააბრუნა სწორი მნიშვნელობა — საიმედო შედეგისთვის ეს საკმარისი არ არის. სცადე თავიდან; თუ ერთი და იგივე უჯრედები აკლია, გამომიგზავნე კავშირის ლოგი „ადაპტერის“ გვერდიდან.",
        HybridFailureKind.TransportLost =>
            "შემოწმების დროს ადაპტერთან კავშირი გაწყდა. თავიდან დააკავშირე ადაპტერი და სცადე ისევ.",
        _ => FallbackStatus
    };
}
