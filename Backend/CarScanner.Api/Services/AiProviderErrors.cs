using System.Text.Json;

namespace CarScanner.Api.Services;

/// <summary>
/// What the app is told when the AI provider (Gemini) fails. The provider's own HTTP status is never forwarded as-is:
/// a provider 401/403 means "the server's key or project is wrong", not "the user is not signed in / has no PRO", and the
/// app maps 401 and 403 to exactly those user-facing messages. Each failure gets a stable code instead.
/// </summary>
public static class AiProviderErrors
{
    public sealed record Failure(int HttpStatus, string Code, string MessageKa, string MessageEn);

    public static Failure Map(int providerStatus, string? providerErrorStatus = null)
    {
        var google = (providerErrorStatus ?? string.Empty).ToUpperInvariant();

        if (providerStatus == 429 || google == "RESOURCE_EXHAUSTED")
            return new(429, "AI_PROVIDER_QUOTA",
                "AI სერვისის ლიმიტი დროებით ამოიწურა. სცადე მოგვიანებით.",
                "The AI service is temporarily over its limit. Try again later.");

        if (providerStatus is 400 or 401 or 403 || google is "PERMISSION_DENIED" or "UNAUTHENTICATED" or "INVALID_ARGUMENT" or "FAILED_PRECONDITION")
            return new(502, "AI_PROVIDER_CONFIG",
                "AI სერვისი სერვერზე სწორად არ არის გამართული (ეს შენს ანგარიშს ან გამოწერას არ ეხება). მხარდაჭერას შეატყობინე.",
                "The AI service is not set up correctly on the server (this is not about your account or subscription). Please tell support.");

        if (providerStatus == 404 || google == "NOT_FOUND")
            return new(502, "AI_MODEL_UNAVAILABLE",
                "AI მოდელი ამჟამად მიუწვდომელია. მხარდაჭერას შეატყობინე.",
                "The AI model is currently unavailable. Please tell support.");

        if (providerStatus is 408 or 504 || google == "DEADLINE_EXCEEDED")
            return new(504, "AI_PROVIDER_TIMEOUT",
                "AI სერვისმა დროზე ვერ უპასუხა. სცადე თავიდან.",
                "The AI service did not answer in time. Try again.");

        if (providerStatus is 500 or 502 or 503 || google is "UNAVAILABLE" or "INTERNAL")
            return new(503, "AI_PROVIDER_UNAVAILABLE",
                "AI სერვისი დროებით მიუწვდომელია. სცადე მოგვიანებით.",
                "The AI service is temporarily unavailable. Try again later.");

        return new(502, "AI_PROVIDER_ERROR",
            "AI სერვისმა მოთხოვნა ვერ დაამუშავა. სცადე მოგვიანებით.",
            "The AI service could not process the request. Try again later.");
    }

    public static Failure NotConfigured { get; } = new(503, "AI_NOT_CONFIGURED",
        "AI სერვისი სერვერზე ჯერ არ არის ჩართული. მხარდაჭერას შეატყობინე.",
        "The AI service is not enabled on the server yet. Please tell support.");

    public static Failure Timeout { get; } = Map(504);

    /// <summary>Google's error status word (for example PERMISSION_DENIED) and a short message from an error body; never the request or the key.</summary>
    public static (string? Status, string? Message) ReadProviderError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var error))
                return (null, null);

            var status = error.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            if (message is { Length: > 300 })
                message = message[..300];
            return (status, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    public static object Body(Failure failure, int? providerStatus = null) => new
    {
        success = false,
        code = failure.Code,
        error = failure.MessageKa + " / " + failure.MessageEn,
        messageKa = failure.MessageKa,
        messageEn = failure.MessageEn,
        providerStatus
    };
}
