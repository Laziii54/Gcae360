using CarScanner.Services.OBD;
using CarScanner.Services.OemDiagnostics;

namespace CarScanner.Services.Mileage;

/// <summary>What a failed (or notable) Original Mileage read looked like, in the words the technical log uses.</summary>
public enum MileageReadCategory
{
    /// <summary>Nothing came back: silence, NO DATA, a timeout, or "response pending" with no final answer.</summary>
    NoReply,

    /// <summary>The module answered with an explicit negative response (7F service code).</summary>
    NegativeResponse,

    /// <summary>The adapter did not understand or accept a command, reported a bus error, or the link was lost.</summary>
    AdapterOrTransport,

    /// <summary>Something arrived that is not this request's answer, arrived incomplete, or could not be decoded.</summary>
    MalformedReply,

    /// <summary>The frames came from another module than the one that was asked.</summary>
    OtherEcu,

    /// <summary>A value was decoded (only given a category when there is something to say about it).</summary>
    Decoded,

    /// <summary>A value that is not usable as evidence of the mileage: not available, implausible, or from a definition still under test.</summary>
    Unverified
}

public sealed record MileageReadObservation(MileageReadCategory Category, string Text)
{
    public string Label => Category switch
    {
        MileageReadCategory.NoReply => "no reply",
        MileageReadCategory.NegativeResponse => "negative response",
        MileageReadCategory.AdapterOrTransport => "adapter/transport",
        MileageReadCategory.MalformedReply => "malformed reply",
        MileageReadCategory.OtherEcu => "other ECU",
        MileageReadCategory.Decoded => "decoded",
        _ => "unverified"
    };

    public override string ToString() => $"[{Label}] {Text}";
}

/// <summary>
/// Words for the technical log of the Original Mileage scan: what each request showed, told apart (no reply, a negative response,
/// an adapter or transport failure, a malformed reply, another ECU answering, a decoded value, a value that is not usable as
/// evidence). It only DESCRIBES what the existing code already received and decided. It sends nothing, it feeds no decision, and
/// no outcome, evidence, confidence or mileage depends on it: the provider uses these strings only in <see cref="MileageTrace"/>
/// lines and in the <c>Detail</c> text of an attempt (which the page does not show).
/// </summary>
public static class MileageReadDiagnosis
{
    /// <summary>The observation for a reply that gave no usable payload (the decoder returned no data).</summary>
    public static MileageReadObservation OfReply(string? raw, int? receiveCanId, int serviceId, int identifier)
    {
        var classified = AdapterReadFailure.ClassifyReply(raw, receiveCanId, identifier, (byte)serviceId);
        var category = classified.Kind switch
        {
            AdapterFailureKind.NoReplyInTime or AdapterFailureKind.EcuDidNotAnswer => MileageReadCategory.NoReply,
            AdapterFailureKind.EcuRefused => MileageReadCategory.NegativeResponse,
            AdapterFailureKind.AdapterCommandUnsupported or AdapterFailureKind.AdapterLinkLost or AdapterFailureKind.BusOrProtocolError => MileageReadCategory.AdapterOrTransport,
            AdapterFailureKind.ReplyFromAnotherModule => MileageReadCategory.OtherEcu,
            // "Response pending" with no final answer is a missing answer, not a damaged one.
            AdapterFailureKind.IncompleteReply when classified.Text.Contains("response pending", StringComparison.Ordinal) => MileageReadCategory.NoReply,
            _ => MileageReadCategory.MalformedReply
        };

        var text = classified.Kind == AdapterFailureKind.None
            ? "the reply looks like a positive answer, but it could not be read as this request's answer"
            : classified.Text;
        return new MileageReadObservation(category, text);
    }

    /// <summary>
    /// The observation for an exception thrown by the read (a refused setup command, a timeout, a lost link …).
    /// <paramref name="linkDown"/> is the state of the link to the adapter when the request failed: the protocol layer ends its
    /// retries of a request on a dropped link with a timeout error, so only the state of the link tells the two apart.
    /// </summary>
    public static MileageReadObservation OfException(Exception exception, bool linkDown = false)
    {
        if (linkDown)
            return new MileageReadObservation(MileageReadCategory.AdapterOrTransport, "the link to the adapter was down when the request failed");

        var classified = AdapterReadFailure.Classify(exception);
        return classified.Kind switch
        {
            AdapterFailureKind.NoReplyInTime => new(MileageReadCategory.NoReply, classified.Text),
            AdapterFailureKind.Undetermined => new(MileageReadCategory.AdapterOrTransport, $"the request failed with {exception.GetType().Name} (not classified)"),
            _ => new(MileageReadCategory.AdapterOrTransport, classified.Text)
        };
    }

    /// <summary>The observation for a reply whose payload was extracted but whose value could not be decoded.</summary>
    public static MileageReadObservation OfDecodeFailure(OdometerDecodeFailure failure) => failure switch
    {
        OdometerDecodeFailure.TooShort => new(MileageReadCategory.MalformedReply, "the reply is shorter than the signal definition needs"),
        OdometerDecodeFailure.BadScaling => new(MileageReadCategory.MalformedReply, "the signal definition cannot be scaled (offset, length or factor is not usable)"),
        OdometerDecodeFailure.NotAvailable => new(MileageReadCategory.Unverified, "the module reports \"not available\" (every bit set); no value"),
        OdometerDecodeFailure.Implausible => new(MileageReadCategory.Unverified, "the value is outside what an odometer can show; kept for review, never used"),
        _ => new(MileageReadCategory.MalformedReply, "the reply could not be decoded")
    };

    /// <summary>
    /// What is worth saying about a value that WAS decoded, or null when nothing is: the reply came from another ECU than the
    /// addressed module (no reply id was known, so the responder is named and not verified), and/or the signal definition is
    /// still under test. Saying so does not change how the value is used.
    /// </summary>
    public static MileageReadObservation? OfDecodedValue(bool definitionUnderTest, int transmitCanId, int? receiveCanId, int? responderCanId)
    {
        var notes = new List<string>();
        var other = OdometerResponseDecoder.IsResponderForeign(transmitCanId, receiveCanId, responderCanId);
        if (other)
            notes.Add($"the reply came from ECU 0x{responderCanId:X3}, not from the addressed module (request to 0x{transmitCanId:X3}); no reply id was known, so the responder is named but not verified");
        if (definitionUnderTest)
            notes.Add("the signal definition is marked as under test, not yet verified on a car");

        if (notes.Count == 0)
            return null;

        return new MileageReadObservation(other ? MileageReadCategory.OtherEcu : MileageReadCategory.Unverified, "value decoded; " + string.Join("; ", notes));
    }

    // ------------------------------------------------------------------ the log lines (the provider only calls these)

    /// <summary>"…: error (TimeoutException) — [no reply] no reply in time" (the part before the dash is the line the log always had).</summary>
    public static string ErrorLine(string method, Exception exception, bool records, bool linkDown = false) =>
        $"{method}: {(records ? "records error" : "error")} ({exception.GetType().Name}) — {OfException(exception, linkDown)}";

    public static string ErrorDetail(Exception exception, bool linkDown = false) => $"{exception.GetType().Name} — {OfException(exception, linkDown)}";

    /// <summary>"…: Negative — [negative response] ECU refused: … (NRC 0x31)".</summary>
    public static string FailureLine(string method, OdometerDecodeFailure failure, MileageReadObservation observed, bool records) =>
        $"{method}: {(records ? "records " : string.Empty)}{failure} — {observed}";

    /// <summary>The attempt's detail text: what it said before, then what the reply showed.</summary>
    public static string FailureDetail(string existingDetail, MileageReadObservation observed) => $"{existingDetail} — {observed}";

    public static string UndecodedLine(string method, OdometerDecodeFailure? failure, string firstBytesHex, MileageReadObservation observed) =>
        $"{method}: reply could not be decoded ({failure}) {firstBytesHex} — {observed}";

    /// <summary>The value line the log always had, plus the note when there is one.</summary>
    public static string ValueLine(string method, double km, MileageReadObservation? note) =>
        note is null ? $"{method}: {km:0} km" : $"{method}: {km:0} km — {note}";

    public static string ValueDetail(MileageReadObservation? note) => note?.ToString() ?? string.Empty;
}
