using CarScanner.Services.OBD;

namespace CarScanner.Services;

/// <summary>
/// Turns an exception into a short message a driver can understand, in the app
/// language. Never shows .NET text ("Object reference…", socket codes, SQL).
/// Messages that the app itself wrote for the user (InvalidOperationException
/// with Georgian or plain sentence text) are kept as they are.
/// </summary>
public static class UserError
{
    private static string L(string ka, string en) => ObdLang.IsEnglish ? en : ka;

    public static string Describe(Exception? exception)
    {
        if (exception is null)
            return L("რაღაც ვერ შესრულდა. სცადე თავიდან.", "Something went wrong. Please try again.");

        if (exception is AggregateException aggregate && aggregate.InnerException is not null)
            return Describe(aggregate.InnerException);

        return exception switch
        {
            OperationCanceledException => L("შეჩერდა.", "Stopped."),
            TimeoutException => L("მანქანამ ან ადაპტერმა დროულად არ უპასუხა. შეამოწმე კავშირი და სცადე თავიდან.",
                                  "The car or adapter did not answer in time. Check the connection and try again."),
            HttpRequestException => L("სერვერთან დაკავშირება ვერ მოხერხდა. შეამოწმე ინტერნეტი და სცადე თავიდან.",
                                      "Could not reach the server. Check the internet and try again."),
            System.Net.Sockets.SocketException or IOException =>
                L("კავშირი ადაპტერთან გაწყდა. დააკავშირე თავიდან.", "The connection to the adapter was lost. Connect again."),
            UnauthorizedAccessException => L("საჭიროა ნებართვა. ჩართე ის ტელეფონის პარამეტრებში.",
                                              "A permission is needed. Turn it on in the phone settings."),
            InvalidOperationException or ArgumentException or FormatException when IsFriendly(exception.Message) => exception.Message,
            _ => L("რაღაც ვერ შესრულდა. სცადე თავიდან.", "Something went wrong. Please try again.")
        };
    }

    /// <summary>True for messages written by the app for people (not framework text).</summary>
    private static bool IsFriendly(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 400)
            return false;

        // Georgian text is always written by the app.
        foreach (var c in message)
            if (c is >= 'Ⴀ' and <= 'ჿ')
                return true;

        // Framework messages usually contain these.
        string[] technical =
        {
            "Object reference", "Value cannot be null", "Parameter", "Sequence contains",
            "Index was outside", "Collection was modified", "Exception", "System.", "Nullable",
            "socket", "Socket", "SQLite", "HRESULT", "Java.", "Foundation.", "at CarScanner"
        };
        return !technical.Any(t => message.Contains(t, StringComparison.Ordinal));
    }
}
