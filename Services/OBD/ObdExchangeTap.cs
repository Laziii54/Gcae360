using System.Text.RegularExpressions;

namespace CarScanner.Services.OBD;

public enum ObdExchangeOrigin
{
    /// <summary>Addressed module request/answer (ISO-TP / UDS) from the module session.</summary>
    ModuleSession,

    /// <summary>Standard OBD2 request (Mode 01 PID, Mode 03/07/0A) sent through the protocol layer.</summary>
    StandardObd
}

/// <summary>One request/answer pair that the protocol layer already exchanged with the car.</summary>
public sealed record ObdRawExchange(
    int TxCanId,
    int RxCanId,
    bool Extended29Bit,
    string RequestHex,
    string ResponseText,
    ObdExchangeOrigin Origin = ObdExchangeOrigin.ModuleSession,
    bool? IsCan = null);

/// <summary>
/// Read-only observation point. The protocol layer reports every exchange it has already completed;
/// subscribers cannot change or send anything. Nothing is published unless somebody subscribed (the
/// data-collection service does that only after the user's consent), and a failing subscriber never
/// affects diagnostics.
/// </summary>
public static class ObdExchangeTap
{
    /// <summary>The only standard requests that may ever be observed: Mode 01 PIDs and the read-only DTC modes.</summary>
    private static readonly Regex StandardRequest = new("^(01[0-9A-F]{2}|03|07|0A)$", RegexOptions.Compiled);

    public static event Action<ObdRawExchange>? Observed;

    public static bool HasSubscribers => Observed is not null;

    /// <summary>True for Mode 01 PIDs and Modes 03/07/0A. Never true for 04 (clear), 09 (VIN) or any AT command.</summary>
    public static bool IsObservableStandardRequest(string? command) =>
        command is not null && StandardRequest.IsMatch(command.Replace(" ", string.Empty).ToUpperInvariant());

    public static void Publish(ObdRawExchange exchange)
    {
        var handlers = Observed;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try { ((Action<ObdRawExchange>)handler)(exchange); }
            catch { /* observers must never break a diagnostic read */ }
        }
    }
}
