namespace CarScanner.Tests;

/// <summary>
/// Model fallback for /api/ai/chat and /api/ai/diagnose (both route through GenerateGeminiAsync in Program.cs).
/// Production logs on 2026-10-14 showed the default model (gemini-3.8-flash) failing with a transient 503
/// ("usually temporary"), then falling back to gemini-2.5-flash, which Google now answers with 404 ("no longer
/// available to new users") — so every transient hiccup on the primary model was guaranteed to fail outright.
/// Fixed by (1) dropping gemini-2.5-flash from the fallback list, confirmed Stable alternatives in its place,
/// and (2) a three-way classification instead of treating every non-2xx status as "try the next model in the
/// list": Transient (retry the SAME model after a short wait), ModelUnavailable (404 — a genuinely different
/// model may work), and Blocked (400/401/403 — the request or the key itself, not the model, so switching
/// models would not help either).
/// </summary>
public sealed class AiModelFallbackTests
{
    [Fact]
    public void TheDeadModel_IsNoLongerInTheFallbackList()
    {
        Assert.DoesNotContain("gemini-2.5-flash", AppConfig.AllowedModels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheFallbackOrder_TriesAConfirmedStableModel_RightAfterTheDefault()
    {
        var order = AppConfig.GetModelFallbackOrder(AppConfig.DefaultModel).ToArray();
        Assert.Equal(AppConfig.DefaultModel, order[0]);
        // gemini-3.5-flash-lite and gemini-3.1-flash-lite are both confirmed Stable on
        // ai.google.dev/gemini-api/docs/models; gemini-2.5-flash must not reappear anywhere in the order.
        Assert.DoesNotContain("gemini-2.5-flash", order, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("gemini-3.5-flash-lite", order);
        Assert.Contains("gemini-3.1-flash-lite", order);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests)]
    [InlineData(System.Net.HttpStatusCode.InternalServerError)]
    [InlineData(System.Net.HttpStatusCode.BadGateway)]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable)]
    [InlineData(System.Net.HttpStatusCode.GatewayTimeout)]
    public void OverloadAndRateLimitStatuses_AreTransient_WorthRetryingTheSameModel(System.Net.HttpStatusCode status)
    {
        Assert.Equal(AppConfig.FailureKind.Transient, AppConfig.ClassifyFailure(status));
    }

    [Fact]
    public void AnUnknownModel_IsModelUnavailable_AGenuinelyDifferentModelMayWork()
    {
        Assert.Equal(AppConfig.FailureKind.ModelUnavailable, AppConfig.ClassifyFailure(System.Net.HttpStatusCode.NotFound));
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.BadRequest)]
    [InlineData(System.Net.HttpStatusCode.Unauthorized)]
    [InlineData(System.Net.HttpStatusCode.Forbidden)]
    public void ABadRequestOrBadKey_IsBlocked_NotModelSpecific_SwitchingModelsWontHelp(System.Net.HttpStatusCode status)
    {
        Assert.Equal(AppConfig.FailureKind.Blocked, AppConfig.ClassifyFailure(status));
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Program() =>
        File.ReadAllText(Path.Combine(Root(), "Backend", "CarScanner.Api", "Program.cs")).Replace("\r\n", "\n");

    /// <summary>
    /// GeminiClient.CallWithFallbackAsync — the retry/fallback loop, kept as a real class (not a Program.cs
    /// local function) at the end of the file precisely so it's reachable here and in GeminiRetryRuntimeTests.
    /// </summary>
    private static string GeminiMethod(string program)
    {
        var start = program.IndexOf("static class GeminiClient", StringComparison.Ordinal);
        Assert.True(start > 0, "GeminiClient not found");
        return program[start..];
    }

    [Fact]
    public void BothAiRoutes_ShareTheOneCorrectedImplementation()
    {
        var program = Program();
        var diagnoseAt = program.IndexOf("\"/api/ai/diagnose\"", StringComparison.Ordinal);
        var chatAt = program.IndexOf("\"/api/ai/chat\"", StringComparison.Ordinal);
        Assert.True(diagnoseAt > 0 && chatAt > diagnoseAt);

        var between = program[diagnoseAt..chatAt];
        var afterChat = program[chatAt..(chatAt + 400)];
        Assert.Contains("GenerateGeminiAsync(", between);
        Assert.Contains("GenerateGeminiAsync(", afterChat);
    }

    [Fact]
    public void TheAttemptBudget_IsOneCallPerAllowedModel()
    {
        var method = GeminiMethod(Program());
        Assert.Contains("var models = AppConfig.GetModelFallbackOrder(model).ToArray();", method);
        Assert.Contains("for (var attempt = 0; attempt < models.Length; attempt++)", method);
        Assert.Equal(3, AppConfig.AllowedModels.Length);
    }

    [Fact]
    public void OnlyABlockedFailure_StopsTheLoopEarly()
    {
        var method = GeminiMethod(Program());
        var blocked = method.IndexOf("FailureKind.Blocked)", StringComparison.Ordinal);
        Assert.True(blocked > 0, "the Blocked check was not found");
        Assert.Contains("break;", method[blocked..(blocked + 120)]);
        // Transient and unknown-model failures wait briefly and fall through to the next model.
        Assert.Contains("await Task.Delay(AppConfig.TransientRetryDelay, cancellationToken);", method);
    }

    [Fact]
    public void TheWorstCase_StaysInsideTheMobileAppsBudget()
    {
        var method = GeminiMethod(Program());
        Assert.Contains("client.Timeout =\n        TimeSpan.FromSeconds(25);", method);

        var mobile = File.ReadAllText(Path.Combine(Root(), "AI", "AiDiagnosticService.cs"));
        Assert.Contains("Timeout = TimeSpan.FromSeconds(AIConfig.MaxRequestSeconds)", mobile);
        var aiConfig = File.ReadAllText(Path.Combine(Root(), "Services", "AIConfig.cs"));
        Assert.Contains("MaxRequestSeconds = 90", aiConfig);

        // One 25 s attempt per model plus the short waits between them.
        var worst = AppConfig.AllowedModels.Length * 25 + (AppConfig.AllowedModels.Length - 1) * AppConfig.TransientRetryDelay.TotalSeconds;
        Assert.True(worst < 90, $"worst case {worst}s");
    }

    [Fact]
    public void CancellationIsStillRespected_ATransportTimeoutIsNeverTreatedAsUserCancellation()
    {
        var method = GeminiMethod(Program());
        Assert.Contains("catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)", method);
        Assert.Contains("await Task.Delay(AppConfig.TransientRetryDelay, cancellationToken);", method);
    }

    [Fact]
    public void AFailedAiRequest_GivesTheDailyAllowanceBack()
    {
        var program = Program();
        Assert.Contains("usage.RefundDailyAsync(dailyKey", program);
        Assert.Contains("IStatusCodeHttpResult { StatusCode: >= 400 }", program);
    }
}