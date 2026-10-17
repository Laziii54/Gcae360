using System.Net;
using System.Net.Http;
using System.Text;
using CarScanner.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// Runtime coverage for CallGeminiWithFallbackAsync (the Gemini outbound-call loop extracted out of
/// GenerateGeminiAsync) against a stubbed HttpClient — no real network call, no Azure, no account/entitlement
/// setup needed, since this method only ever receives an already-authorized, already-entitled request. Account
/// resolution, EntitlementStore, AdminAccess and the daily/per-minute rate limits all live in GenerateGeminiAsync,
/// above the method under test here, and are untouched by this file.
/// </summary>
public sealed class GeminiRetryRuntimeTests
{
    private const string SuccessBody = """{"candidates":[{"content":{"parts":[{"text":"Hello from Gemini"}]}}]}""";

    // Shaped exactly like the real Gemini error bodies captured in production on 2026-10-14.
    private const string Overloaded503 =
        """{"error":{"code":503,"status":"UNAVAILABLE","message":"This model is currently experiencing high demand. Spikes in demand are usually temporary. Please try again later."}}""";
    private const string ModelGone404 =
        """{"error":{"code":404,"status":"NOT_FOUND","message":"This model models/gemini-2.5-flash is no longer available to new users. Please update your code to use models/gemini-3.8-flash for the latest features and improvements."}}""";
    private const string RateLimited429 =
        """{"error":{"code":429,"status":"RESOURCE_EXHAUSTED","message":"Rate limit exceeded, retry shortly."}}""";

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string ErrorBody(int code, string status, string message) =>
        "{\"error\":{\"code\":" + code + ",\"status\":\"" + status + "\",\"message\":\"" + message + "\"}}";

    /// <summary>Captures every logged message so a test can assert on diagnostics without a real logging sink.</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    /// <summary>Returns scripted responses in order and records every requested URL (which names the model).</summary>
    private sealed class ScriptedGeminiHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>[] _responses;
        private int _index;

        public List<string> RequestedUris { get; } = new();

        /// <summary>Runs after each scripted response is produced — used to simulate the caller cancelling mid-retry.</summary>
        public Action? AfterSend { get; set; }

        public ScriptedGeminiHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!.ToString());
            var response = _responses[_index++](request);
            AfterSend?.Invoke();
            return Task.FromResult(response);
        }
    }

    private static async Task<(int StatusCode, string Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };

        await result.ExecuteAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static Task<IResult> CallAsync(HttpClient client, string model = "gemini-3.8-flash", CancellationToken ct = default) =>
        GeminiClient.CallWithFallbackAsync(client, "test-key", model, "hello", NullLogger.Instance, ct);

    [Fact]
    public async Task PrimaryModel503_ThenTheNextModelAnswers()
    {
        var handler = new ScriptedGeminiHandler(
            _ => Json(HttpStatusCode.ServiceUnavailable, Overloaded503),
            _ => Json(HttpStatusCode.OK, SuccessBody));
        using var client = new HttpClient(handler);

        var (status, body) = await ExecuteAsync(await CallAsync(client));

        Assert.Equal(200, status);
        Assert.Contains("Hello from Gemini", body);
        Assert.Equal(2, handler.RequestedUris.Count);
        Assert.NotEqual(handler.RequestedUris[0], handler.RequestedUris[1]);        // overload moves on to ANOTHER model
        Assert.Contains("gemini-3.8-flash", handler.RequestedUris[0]);
    }

    [Fact]
    public async Task PrimaryModel404_ThenTheDistinctFallbackModelSucceeds()
    {
        var handler = new ScriptedGeminiHandler(
            _ => Json(HttpStatusCode.NotFound, ModelGone404),
            _ => Json(HttpStatusCode.OK, SuccessBody));
        using var client = new HttpClient(handler);

        var (status, body) = await ExecuteAsync(await CallAsync(client, AppConfig.DefaultModel));

        Assert.Equal(200, status);
        Assert.Contains("Hello from Gemini", body);
        Assert.Equal(2, handler.RequestedUris.Count);
        Assert.NotEqual(handler.RequestedUris[0], handler.RequestedUris[1]);        // switched to a different model
        Assert.Contains(AppConfig.DefaultModel, handler.RequestedUris[0]);
        var expectedFallback = AppConfig.GetModelFallbackOrder(AppConfig.DefaultModel).Skip(1).First();
        Assert.Contains(expectedFallback, handler.RequestedUris[1]);
    }

    [Fact]
    public async Task EveryModelFails_ReturnsTheMappedFailure_AfterOneAttemptPerModel()
    {
        var handler = new ScriptedGeminiHandler(
            _ => Json(HttpStatusCode.ServiceUnavailable, Overloaded503),
            _ => Json(HttpStatusCode.ServiceUnavailable, Overloaded503),
            _ => Json(HttpStatusCode.ServiceUnavailable, Overloaded503));
        using var client = new HttpClient(handler);

        var (status, body) = await ExecuteAsync(await CallAsync(client));

        Assert.Equal(AppConfig.AllowedModels.Length, handler.RequestedUris.Count);  // each allowed model once, never more
        Assert.Equal(503, status);
        Assert.Contains("AI_PROVIDER_UNAVAILABLE", body);
    }

    [Fact]
    public async Task ATemporaryRateLimit_RecoversOnTheNextModel()
    {
        var handler = new ScriptedGeminiHandler(
            _ => Json(HttpStatusCode.TooManyRequests, RateLimited429),
            _ => Json(HttpStatusCode.OK, SuccessBody));
        using var client = new HttpClient(handler);

        var (status, _) = await ExecuteAsync(await CallAsync(client));

        Assert.Equal(200, status);
        Assert.Equal(2, handler.RequestedUris.Count);
        Assert.NotEqual(handler.RequestedUris[0], handler.RequestedUris[1]);       // 429 on one model: the next model is asked
    }

    [Fact]
    public async Task APersistentRateLimit_SurfacesAsAQuotaFailure_DistinctFromAGenericError()
    {
        var handler = new ScriptedGeminiHandler(
            _ => Json(HttpStatusCode.TooManyRequests, RateLimited429),
            _ => Json(HttpStatusCode.TooManyRequests, RateLimited429),
            _ => Json(HttpStatusCode.TooManyRequests, RateLimited429));
        using var client = new HttpClient(handler);

        var (status, body) = await ExecuteAsync(await CallAsync(client));

        Assert.Equal(429, status);
        Assert.Contains("AI_PROVIDER_QUOTA", body);                                // the body names it as quota, not a generic failure
        Assert.Equal(3, handler.RequestedUris.Count);
    }

    [Fact]
    public async Task CancellationDuringTheRetryWait_StopsImmediately_WithNoSecondAttempt()
    {
        using var cts = new CancellationTokenSource();
        var handler = new ScriptedGeminiHandler(_ => Json(HttpStatusCode.ServiceUnavailable, Overloaded503))
        {
            // Simulate the caller cancelling right as the first (transient) response comes back,
            // i.e. before the short backoff wait that would otherwise lead to a second attempt.
            AfterSend = () => cts.Cancel()
        };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CallAsync(client, ct: cts.Token));

        Assert.Single(handler.RequestedUris);                                      // retry never happened
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHENTICATED")]
    [InlineData(HttpStatusCode.Forbidden, "PERMISSION_DENIED")]
    public async Task AnAuthOrPermissionFailure_StopsImmediately_NeverRetriesOrSwitchesModels(HttpStatusCode status, string googleStatus)
    {
        var handler = new ScriptedGeminiHandler(
            _ => Json(status, ErrorBody((int)status, googleStatus, "Bad or missing key.")));
        using var client = new HttpClient(handler);

        var (responseStatus, body) = await ExecuteAsync(await CallAsync(client));

        // Not model-specific: the key/request is the problem, so there must be no retry and no fallback attempt.
        Assert.Single(handler.RequestedUris);
        Assert.Equal(502, responseStatus);
        Assert.Contains("AI_PROVIDER_CONFIG", body);
    }

    [Fact]
    public async Task APrimaryModel404_PreservesTheOriginalErrorForDiagnostics_EvenIfTheFallbackAlsoFails()
    {
        var handler = new ScriptedGeminiHandler(
            _ => Json(HttpStatusCode.NotFound, ModelGone404),
            _ => Json(HttpStatusCode.ServiceUnavailable, Overloaded503),
            _ => Json(HttpStatusCode.ServiceUnavailable, Overloaded503));
        using var client = new HttpClient(handler);
        var logger = new CapturingLogger();

        var (status, body) = await ExecuteAsync(
            await GeminiClient.CallWithFallbackAsync(client, "test-key", AppConfig.DefaultModel, "hello", logger, CancellationToken.None));

        Assert.Equal(3, handler.RequestedUris.Count);
        // The response the client sees reflects the LAST attempt (the last model's 503)...
        Assert.Equal(503, status);
        Assert.Contains("AI_PROVIDER_UNAVAILABLE", body);
        // ...but the original 404 on the primary model is not lost: it is still a distinct, separate log line.
        Assert.Contains(logger.Messages, m => m.Contains(AppConfig.DefaultModel) && m.Contains("404"));
        Assert.Contains(logger.Messages, m => m.Contains("503"));
    }

    [Fact]
    public async Task ARateLimit429_WithARetryAfterHeader_IsDiagnosedAsATemporaryThrottle()
    {
        var handler = new ScriptedGeminiHandler(
            _ =>
            {
                var response = Json(HttpStatusCode.TooManyRequests, RateLimited429);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
                return response;
            },
            _ => Json(HttpStatusCode.OK, SuccessBody));
        using var client = new HttpClient(handler);
        var logger = new CapturingLogger();

        var (status, _) = await ExecuteAsync(
            await GeminiClient.CallWithFallbackAsync(client, "test-key", AppConfig.DefaultModel, "hello", logger, CancellationToken.None));

        Assert.Equal(200, status);                                                 // retry policy is unchanged either way
        Assert.Equal(2, handler.RequestedUris.Count);
        Assert.Contains(logger.Messages, m => m.Contains("retryAfter=5"));         // Google's own hint: short, bounded throttle
    }

    [Fact]
    public async Task ARateLimit429_WithNoRetryAfterHeader_IsDiagnosedWithoutOne()
    {
        var handler = new ScriptedGeminiHandler(
            _ => Json(HttpStatusCode.TooManyRequests, RateLimited429),             // no Retry-After header
            _ => Json(HttpStatusCode.OK, SuccessBody));
        using var client = new HttpClient(handler);
        var logger = new CapturingLogger();

        var (status, _) = await ExecuteAsync(
            await GeminiClient.CallWithFallbackAsync(client, "test-key", AppConfig.DefaultModel, "hello", logger, CancellationToken.None));

        Assert.Equal(200, status);                                                 // same retry policy — no behavior change
        Assert.Equal(2, handler.RequestedUris.Count);
        Assert.Contains(logger.Messages, m => m.Contains("retryAfter=none"));      // no hint: possibly a harder quota, not just a blip
    }

    private static string Reply(string parts, string finish = "STOP") =>
        "{\"candidates\":[{\"content\":{\"parts\":" + parts + "},\"finishReason\":\"" + finish + "\"}]}";

    [Fact]
    public async Task ABlockedPromptWithNoCandidates_TriesTheNextModel_InsteadOfFailingWithAParseError()
    {
        var handler = new ScriptedGeminiHandler(
            _ => Json(HttpStatusCode.OK, """{"promptFeedback":{"blockReason":"OTHER"}}"""),
            _ => Json(HttpStatusCode.OK, SuccessBody));
        using var client = new HttpClient(handler);

        var (status, body) = await ExecuteAsync(await CallAsync(client));

        Assert.Equal(200, status);
        Assert.Contains("Hello from Gemini", body);
        Assert.Equal(2, handler.RequestedUris.Count);
    }

    [Fact]
    public async Task AReplyMadeOnlyOfReasoning_IsNotShown_AndTheNextModelIsAsked()
    {
        var handler = new ScriptedGeminiHandler(
            _ => Json(HttpStatusCode.OK, Reply("[{\"text\":\"internal\",\"thought\":true}]", "MAX_TOKENS")),
            _ => Json(HttpStatusCode.OK, SuccessBody));
        using var client = new HttpClient(handler);

        var (status, body) = await ExecuteAsync(await CallAsync(client));

        Assert.Equal(200, status);
        Assert.DoesNotContain("internal", body);
        Assert.Contains("Hello from Gemini", body);
    }

    [Fact]
    public async Task EveryModelReturningNothing_IsReportedAsUnavailable_NotAsASuccessfulEmptyAnswer()
    {
        var handler = new ScriptedGeminiHandler(
            _ => Json(HttpStatusCode.OK, Reply("[]")),
            _ => Json(HttpStatusCode.OK, Reply("[]")),
            _ => Json(HttpStatusCode.OK, Reply("[]")));
        using var client = new HttpClient(handler);

        var (status, body) = await ExecuteAsync(await CallAsync(client));

        Assert.Equal(503, status);
        Assert.Contains("AI_PROVIDER_UNAVAILABLE", body);
    }

    [Fact]
    public async Task ACutOffReplyThatHasText_IsKept()
    {
        var handler = new ScriptedGeminiHandler(
            _ => Json(HttpStatusCode.OK, Reply("[{\"text\":\"Partial answer\"}]", "MAX_TOKENS")));
        using var client = new HttpClient(handler);

        var (status, body) = await ExecuteAsync(await CallAsync(client));

        Assert.Equal(200, status);
        Assert.Contains("Partial answer", body);
        Assert.Single(handler.RequestedUris);
    }
}