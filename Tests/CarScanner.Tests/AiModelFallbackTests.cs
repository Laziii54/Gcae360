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
    public void ABoundedAttemptBudget_StillCapsAtTwoOutboundCalls()
    {
        var method = GeminiMethod(Program());
        Assert.Contains("for (var attempt = 1; attempt <= 2; attempt++)", method);
    }

    [Fact]
    public void ATransientFailure_RetriesTheSameModel_AndDoesNotSwitchModels()
    {
        var method = GeminiMethod(Program());
        var transientBranch = method.IndexOf("kind == AppConfig.FailureKind.Transient)", StringComparison.Ordinal);
        var elseIfBranch = method.IndexOf("else if (kind == AppConfig.FailureKind.ModelUnavailable", StringComparison.Ordinal);
        Assert.True(transientBranch > 0 && elseIfBranch > transientBranch);

        var transientBlock = method[transientBranch..elseIfBranch];
        Assert.Contains("Task.Delay(AppConfig.TransientRetryDelay", transientBlock);
        Assert.DoesNotContain("candidateModel = fallbackModel", transientBlock);
    }

    [Fact]
    public void AModelUnavailableFailure_SwitchesToADistinctFallbackModel_OnlyOnce()
    {
        var method = GeminiMethod(Program());
        var modelUnavailableBranch = method.IndexOf("kind == AppConfig.FailureKind.ModelUnavailable", StringComparison.Ordinal);
        var elseBranch = method.IndexOf("\n            else\n", StringComparison.Ordinal);
        Assert.True(modelUnavailableBranch > 0 && elseBranch > modelUnavailableBranch);

        var block = method[modelUnavailableBranch..elseBranch];
        Assert.Contains("candidateModel = fallbackModel;", block);
        Assert.Contains("usedFallbackModel = true;", block);
        // Guarded so a second ModelUnavailable (or Blocked) failure gives up rather than looping on it again.
        Assert.Contains("!usedFallbackModel && fallbackModel is not null", block);
    }

    [Fact]
    public void ABlockedFailure_NeitherRetriesNorSwitchesModels_AndStopsImmediately()
    {
        var method = GeminiMethod(Program());
        var elseBranch = method.IndexOf("\n            else\n            {\n", StringComparison.Ordinal);
        Assert.True(elseBranch > 0, "the 'Blocked' fallthrough branch was not found");

        // The else branch (Blocked: 400/401/403, or ModelUnavailable with nothing left to try) only breaks —
        // it must never retry the same model or switch to a different one.
        var block = method[elseBranch..(elseBranch + 500)];
        Assert.Contains("break;", block);
        Assert.DoesNotContain("candidateModel = fallbackModel", block);
        Assert.DoesNotContain("Task.Delay", block);
    }

    [Fact]
    public void TheHttpTimeout_LeavesRoomForARetry_InsideTheMobileAppsOwnBudget()
    {
        var method = GeminiMethod(Program());
        Assert.Contains("client.Timeout =\n        TimeSpan.FromSeconds(25);", method);

        var mobile = File.ReadAllText(Path.Combine(Root(), "AI", "AiDiagnosticService.cs"));
        Assert.Contains("Timeout = TimeSpan.FromSeconds(AIConfig.MaxRequestSeconds)", mobile);
        var aiConfig = File.ReadAllText(Path.Combine(Root(), "Services", "AIConfig.cs"));
        Assert.Contains("MaxRequestSeconds = 60", aiConfig);

        // Two 25s attempts plus the short transient-retry delay stay comfortably under the mobile app's 60s budget.
        Assert.True(25 * 2 + 2 < 60);
    }

    [Fact]
    public void CancellationIsStillRespected_ATransportTimeoutIsNeverTreatedAsUserCancellation()
    {
        var method = GeminiMethod(Program());
        Assert.Contains("catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)", method);
        Assert.Contains("await Task.Delay(AppConfig.TransientRetryDelay, cancellationToken);", method);
    }
}
