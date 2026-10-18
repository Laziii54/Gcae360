using System.Net.Http.Json;
using System.Text.Json;

namespace CarScanner.Api.Services;

public enum AvatarVerdict
{
    /// <summary>Fine for a public profile picture.</summary>
    Safe,
    /// <summary>Offensive or not allowed (nudity, sexual content, violence, hate, drugs, weapons ...).</summary>
    Unsafe,
    /// <summary>The check could not be done (no key, provider down, unclear answer).</summary>
    Unavailable
}

/// <summary>
/// Automatic check of a profile picture before it is shown on the public ranking. It asks the same Gemini key that
/// powers AI diagnostics to classify the image (the models are tried one after another like for AI answers). A
/// picture that the provider itself refuses to look at for safety reasons counts as unsafe.
/// </summary>
public static class AvatarModeration
{
    public const string Prompt =
        "You review profile pictures for a public car-app leaderboard shown to all ages. " +
        "Answer with exactly one word. UNSAFE if the picture contains nudity, sexual content, graphic violence or gore, " +
        "hate symbols, drugs, weapons aimed at people, self-harm, a child in a suggestive or unsafe context, " +
        "insulting or abusive text, or any content a reasonable person would find offensive. " +
        "SAFE otherwise (cars, people in normal clothing, logos, animals, landscapes, drawings and similar are SAFE).";

    public static async Task<AvatarVerdict> CheckAsync(
        HttpClient client,
        string apiKey,
        byte[] jpeg,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        client.Timeout = TimeSpan.FromSeconds(20);
        var base64 = Convert.ToBase64String(jpeg);

        foreach (var model in AppConfig.GetModelFallbackOrder(AppConfig.DefaultModel))
        {
            var uri = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
            request.Content = JsonContent.Create(new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { text = Prompt },
                            new { inlineData = new { mimeType = "image/jpeg", data = base64 } }
                        }
                    }
                },
                // The provider must not refuse to answer about an offensive picture: we want the verdict, so its own
                // filter is turned off here (a blocked reply is still treated as unsafe below).
                safetySettings = new[]
                {
                    new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
                    new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
                    new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
                    new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" }
                },
                generationConfig = new { maxOutputTokens = 512, temperature = 0 }
            });

            HttpResponseMessage response;
            string body;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Avatar check with {Model} failed: {Error}", model, ex.GetType().Name);
                continue;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Avatar check with {Model} answered HTTP {Status}", model, (int)response.StatusCode);
                    // A bad key / request is not model specific: stop instead of trying the others.
                    if (AppConfig.ClassifyFailure(response.StatusCode) == AppConfig.FailureKind.Blocked)
                        return AvatarVerdict.Unavailable;
                    continue;
                }
            }

            var verdict = ReadVerdict(body);
            if (verdict is not null)
                return verdict.Value;
        }

        return AvatarVerdict.Unavailable;
    }

    /// <summary>Turns a Gemini reply into a verdict; null when the reply is unclear (the caller tries the next model).</summary>
    public static AvatarVerdict? ReadVerdict(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);

            // The provider refused to look at the picture for safety reasons: that itself is the answer.
            if (doc.RootElement.TryGetProperty("promptFeedback", out var feedback) &&
                feedback.TryGetProperty("blockReason", out _))
            {
                return AvatarVerdict.Unsafe;
            }

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            {
                return null;
            }

            var candidate = candidates[0];
            if (candidate.TryGetProperty("finishReason", out var finish) &&
                finish.ValueKind == JsonValueKind.String &&
                finish.GetString() is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "IMAGE_SAFETY")
            {
                return AvatarVerdict.Unsafe;
            }

            var text = new System.Text.StringBuilder();
            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                        continue;
                    if (part.TryGetProperty("text", out var piece) && piece.ValueKind == JsonValueKind.String)
                        text.Append(piece.GetString());
                }
            }

            var answer = text.ToString().Trim().ToUpperInvariant();
            if (answer.Contains("UNSAFE"))
                return AvatarVerdict.Unsafe;
            if (answer.StartsWith("SAFE"))
                return AvatarVerdict.Safe;
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
