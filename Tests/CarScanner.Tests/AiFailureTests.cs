using System.Text.Json;
using CarScanner.Api.Services;
using CarScanner.Services;

namespace CarScanner.Tests;

/// <summary>
/// AI failures must say what really failed. A Gemini 401/403 (wrong or restricted key on the server) used to be forwarded as
/// 401/403, which the app shows as "sign in again" or "you need PRO". These checks run the mapping and the app text; they do not
/// call Gemini and say nothing about whether the production key works.
/// </summary>
public sealed class AiFailureTests
{
    private static string Root
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "CarScanner.csproj")))
                    return dir;
                dir = Path.GetDirectoryName(dir) ?? dir;
            }

            throw new DirectoryNotFoundException("repository root not found");
        }
    }

    [Theory]
    [InlineData(401, null)]
    [InlineData(403, "PERMISSION_DENIED")]
    [InlineData(400, "INVALID_ARGUMENT")]
    public void AProviderAuthOrKeyProblem_IsNeverForwardedAs_401Or403(int providerStatus, string? googleStatus)
    {
        var failure = AiProviderErrors.Map(providerStatus, googleStatus);
        Assert.Equal("AI_PROVIDER_CONFIG", failure.Code);
        Assert.Equal(502, failure.HttpStatus);                                     // the app maps 401 / 403 to "sign in" / "PRO required"
        Assert.Contains("not about your account", failure.MessageEn);
    }

    [Theory]
    [InlineData(429, null, 429, "AI_PROVIDER_QUOTA")]
    [InlineData(200, "RESOURCE_EXHAUSTED", 429, "AI_PROVIDER_QUOTA")]
    [InlineData(404, "NOT_FOUND", 502, "AI_MODEL_UNAVAILABLE")]
    [InlineData(504, null, 504, "AI_PROVIDER_TIMEOUT")]
    [InlineData(503, "UNAVAILABLE", 503, "AI_PROVIDER_UNAVAILABLE")]
    [InlineData(500, null, 503, "AI_PROVIDER_UNAVAILABLE")]
    [InlineData(418, null, 502, "AI_PROVIDER_ERROR")]
    public void EveryOtherProviderFailure_GetsItsOwnCode(int providerStatus, string? googleStatus, int httpStatus, string code)
    {
        var failure = AiProviderErrors.Map(providerStatus, googleStatus);
        Assert.Equal(code, failure.Code);
        Assert.Equal(httpStatus, failure.HttpStatus);
        Assert.False(string.IsNullOrWhiteSpace(failure.MessageKa));
        Assert.False(string.IsNullOrWhiteSpace(failure.MessageEn));
    }

    [Fact]
    public void TheProviderErrorBody_IsReadForItsStatusWord_AndNeverEchoesAKey()
    {
        const string body = """{"error":{"code":403,"message":"Your API key was reported as leaked.","status":"PERMISSION_DENIED"}}""";
        var (status, message) = AiProviderErrors.ReadProviderError(body);
        Assert.Equal("PERMISSION_DENIED", status);
        Assert.Contains("leaked", message);
        Assert.Equal((null, null), AiProviderErrors.ReadProviderError("not json"));
        Assert.Equal((null, null), AiProviderErrors.ReadProviderError(null));

        var json = JsonSerializer.Serialize(AiProviderErrors.Body(AiProviderErrors.Map(403, status), 403));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("AI_PROVIDER_CONFIG", doc.RootElement.GetProperty("code").GetString());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.DoesNotContain("leaked", json);                                      // the provider's message stays in the server log
    }

    [Fact]
    public void TheApp_ShowsTheServersAiMessage_InTheRightLanguage()
    {
        var json = JsonSerializer.Serialize(AiProviderErrors.Body(AiProviderErrors.Map(403, "PERMISSION_DENIED"), 403));
        Assert.Contains("not set up correctly", AiFailureText.FromBody(json, english: true));
        Assert.Contains("სწორად არ არის გამართული", AiFailureText.FromBody(json, english: false));
    }

    [Fact]
    public void TheApp_FallsBackToTheStatusText_WhenTheBodyIsNotAnAiFailure()
    {
        Assert.Null(AiFailureText.FromBody("", true));
        Assert.Null(AiFailureText.FromBody("<html>502</html>", true));
        Assert.Null(AiFailureText.FromBody("""{"success":false,"code":"SESSION_EXPIRED","message":"Session expired. Sign in again."}""", true));
        // the daily limit already carries its own bilingual text
        Assert.Contains("Daily AI limit", AiFailureText.FromBody("""{"code":"AI_DAILY_LIMIT","error":"დღიური ლიმიტი / Daily AI limit reached."}""", true));
    }

    [Fact]
    public void TheEndpoint_ChecksTheKeyBeforeCountingTheRequest_AndNoLongerForwardsTheProviderStatus()
    {
        var program = File.ReadAllText(Path.Combine(Root, "Backend", "CarScanner.Api", "Program.cs")).Replace("\r\n", "\n");
        var method = program.Substring(program.IndexOf("static async Task<IResult> GenerateGeminiAsync", StringComparison.Ordinal));
        method = method.Substring(0, method.IndexOf("// VIN REPORT", StringComparison.Ordinal));

        Assert.True(method.IndexOf("AiProviderErrors.NotConfigured", StringComparison.Ordinal) < method.IndexOf("TryConsumeDailyAsync", StringComparison.Ordinal),
            "a missing server key must not use up the person's daily AI requests");
        Assert.Contains("AiProviderErrors.Map(providerStatus", method);
        Assert.DoesNotContain("statusCode:\n                providerStatus is >= 400", method);
        Assert.Contains("catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException", method);   // a provider timeout is no longer an unhandled 500
        Assert.DoesNotContain("api" + "Key}", method);                              // the key is never interpolated into a log or message
        Assert.Contains("AiFailureText.FromBody", File.ReadAllText(Path.Combine(Root, "AI", "AiDiagnosticService.cs")));
    }
}
