using System.Text.Json;

namespace CarScanner.Services;

/// <summary>
/// Turns the server's AI failure answer (a JSON body with a stable "AI_..." code and a message in both languages) into the
/// text shown to the person. Without this the app only had the HTTP status, and a provider problem on the server looked like
/// "sign in again" (401) or "you need PRO" (403).
/// </summary>
public static class AiFailureText
{
    /// <summary>The message for the person, or null when the body is not an AI failure answer (the status-based text is used then).</summary>
    public static string? FromBody(string? body, bool english)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 20_000)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String ||
                code.GetString() is not { } value || !value.StartsWith("AI_", StringComparison.Ordinal))
                return null;

            var key = english ? "messageEn" : "messageKa";
            if (root.TryGetProperty(key, out var message) && message.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(message.GetString()))
                return message.GetString();

            return root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
