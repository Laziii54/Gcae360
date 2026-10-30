namespace CarScanner.Tests;

/// <summary>
/// User report, 2026-10-31: "confirm your code" e-mails kept arriving despite never opening the app.
/// Root cause: /api/web/checkout/code is a public, unauthenticated, e-mail-only endpoint (no password —
/// by design, since an account made via Google/Apple sign-in never has one) on the public /pro page, so
/// anything that fills in a public form and submits it — not necessarily the account owner — can trigger
/// a real code e-mail. Program.cs and WebPages.cs are MAUI/ASP.NET host files with no existing HTTP-level
/// test harness in this project (no WebApplicationFactory reference), so this reads the source to confirm
/// the honeypot's three pieces stay wired together: the hidden field in the page, the JS that sends its
/// value, and the server check that short-circuits before the rate limiter or any account lookup.
/// </summary>
public sealed class WebCheckoutCodeHoneypotTests
{
    private static string ProgramSource() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory.Split("Tests")[0], "Backend", "CarScanner.Api", "Program.cs"));

    private static string WebPagesSource() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory.Split("Tests")[0], "Backend", "CarScanner.Api", "Services", "WebPages.cs"));

    [Fact]
    public void WebCodeRequest_CarriesTheHoneypotField()
    {
        Assert.Contains("record WebCodeRequest(string? Email, string? Website);", ProgramSource());
    }

    [Fact]
    public void TheEndpoint_ChecksTheHoneypot_BeforeTheRateLimiterOrAnyAccountLookup()
    {
        var code = ProgramSource();
        var method = code[code.IndexOf("\"/api/web/checkout/code\"", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("app.MapPost(\n    \"/api/web/checkout\"", StringComparison.Ordinal)];

        var honeypotCheck = method.IndexOf("string.IsNullOrEmpty(request.Website)", StringComparison.Ordinal);
        var rateLimitCheck = method.IndexOf("RateWindow.TryConsume(\"web-checkout-code:", StringComparison.Ordinal);
        var accountLookup = method.IndexOf("FindByEmailAsync(", StringComparison.Ordinal);

        Assert.True(honeypotCheck > 0, "Honeypot check not found on /api/web/checkout/code.");
        Assert.True(honeypotCheck < rateLimitCheck, "Honeypot must be checked before the rate limiter spends a slot on it.");
        Assert.True(honeypotCheck < accountLookup, "Honeypot must be checked before any account lookup.");

        // A caught bot gets an ordinary-looking success, not an error it could learn from.
        var afterCheck = method[honeypotCheck..];
        Assert.Contains("Results.Ok(new { success = true })", afterCheck[..Math.Min(afterCheck.Length, 200)]);
    }

    [Fact]
    public void ThePage_HasAHiddenFieldThatARealVisitorNeverFills()
    {
        var html = WebPagesSource();
        Assert.Contains("class=\"hp\"", html);
        Assert.Contains("id=\"hpWebsite\"", html);
        Assert.Contains("tabindex=\"-1\"", html);
        Assert.Contains("aria-hidden=\"true\"", html);

        // Off-screen, not merely display:none — some scripts that fill forms skip display:none fields.
        Assert.Contains(".hp{position:absolute;left:-9999px", html);
    }

    [Fact]
    public void TheRequestCodeCall_SendsTheHoneypotFieldAlongWithTheEmail()
    {
        var js = WebPagesSource();
        Assert.Contains("hpWebsite=document.getElementById('hpWebsite')", js);
        Assert.Contains("JSON.stringify({email,website:hpWebsite.value})", js);
    }
}
