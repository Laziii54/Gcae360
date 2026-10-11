using System.Globalization;
using System.Text.RegularExpressions;
using CarScanner.Api.Services.Email;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

internal sealed class EmailClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    public DateTimeOffset Read() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

internal sealed class CapturingSender : IEmailSender
{
    public bool IsConfigured { get; set; } = true;
    public Exception? Fail { get; set; }
    public List<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (Fail is not null) throw Fail;
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class CapturingLog : ILogger<EmailVerificationService>
{
    public List<string> Lines { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add(formatter(state, exception));
}

/// <summary>The branded email: structure, languages, accessibility, safety. Pure rendering, no network.</summary>
public sealed class VerificationEmailTemplateTests
{
    // Text is HTML-encoded in the markup (apostrophes, accented letters, "·" become entities): compare decoded text.
    private static string Decode(string html) => System.Net.WebUtility.HtmlDecode(html);

    private static VerificationEmailModel Model(string lang = "en", string code = "482915", int minutes = 15,
        string? logo = "https://api.example.test/assets/logo.png", string? link = "https://api.example.test/verify-email?l=en&t=TOKENTOKENTOKENTOKENTOKENTOKENTOKEN1",
        string? support = "support@example.test") =>
        new(code, minutes, lang, logo, link, support, 2026);

    [Theory]
    [InlineData("ka")] [InlineData("en")] [InlineData("tr")] [InlineData("de")] [InlineData("pl")]
    [InlineData("es")] [InlineData("pt")] [InlineData("fr")] [InlineData("it")]
    public void EveryAppLanguage_RendersACompleteEmail(string lang)
    {
        var mail = VerificationEmailTemplate.Render(Model(lang));

        Assert.False(string.IsNullOrWhiteSpace(mail.Subject));
        Assert.Contains($"<html lang=\"{lang}\"", mail.Html);
        Assert.Contains("482915", mail.Html);
        Assert.Contains("482915", mail.PlainText);
        Assert.Contains("15", mail.PlainText);                                  // the expiry minutes
        Assert.DoesNotContain("{0}", mail.Html);                                // every placeholder was filled
        Assert.DoesNotContain("{0}", mail.PlainText);
        Assert.DoesNotContain("{0}", mail.Subject);
    }

    [Fact]
    public void Georgian_UsesTheRequestedWords()
    {
        var mail = VerificationEmailTemplate.Render(Model("ka"));
        Assert.Contains("დაადასტურე შენი ელფოსტა", mail.Html);
        Assert.Contains("შენი დამადასტურებელი კოდია", mail.Html);
        Assert.Contains("მონიშნე კოდი და შეიყვანე GCar360 აპლიკაციაში.", mail.Html);
        Assert.Contains("დაადასტურე ელფოსტა", mail.Html);                      // the button
    }

    [Fact]
    public void English_UsesTheRequestedHeading()
    {
        Assert.Contains("Verify your email address", VerificationEmailTemplate.Render(Model("en")).Html);
    }

    [Fact]
    public void OtherLanguages_AreTranslated_NotEnglishCopies()
    {
        var english = VerificationEmailTemplate.Render(Model("en"));
        foreach (var lang in VerificationEmailTemplate.Languages.Where(l => l != "en"))
        {
            var other = VerificationEmailTemplate.Render(Model(lang));
            Assert.NotEqual(english.Subject, other.Subject);
            Assert.NotEqual(english.PlainText, other.PlainText);
        }
    }

    [Theory]
    [InlineData(null, "en")] [InlineData("", "en")] [InlineData("xx", "en")] [InlineData("KA", "ka")]
    [InlineData("de-DE", "de")] [InlineData("pt-BR", "pt")] [InlineData("ka-GE", "ka")]
    public void LanguageResolution(string? requested, string expected) =>
        Assert.Equal(expected, VerificationEmailTemplate.ResolveLanguage(requested));

    [Fact]
    public void TheCode_IsPlainSelectableText_InOneElement_WithoutSeparators()
    {
        var html = VerificationEmailTemplate.Render(Model(code: "073519")).Html;
        var match = Regex.Match(html, "<span class=\"code\"[^>]*>([^<]*)</span>");
        Assert.True(match.Success);
        Assert.Equal("073519", match.Groups[1].Value);                          // copying it gives exactly the digits
        Assert.Contains("user-select:all", html);                               // one tap selects it where supported
        Assert.Contains("letter-spacing:10px", html);                           // spaced by CSS, not by extra characters
    }

    [Fact]
    public void TheClipboardIcon_IsOnlyAVisualCue_WithNoScriptOrFakeCopyButton()
    {
        var html = VerificationEmailTemplate.Render(Model()).Html;
        Assert.Contains("&#128203;", html);                                     // 📋 as text, works with images blocked
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clipboard", html.Replace("&#128203;", ""), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<button", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<form", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);   // no external stylesheet
    }

    [Fact]
    public void TheLogo_IsTheOnlyImage_WithAltText_AndThereIsNoTrackingPixel()
    {
        var html = VerificationEmailTemplate.Render(Model()).Html;
        var images = Regex.Matches(html, "<img\\b[^>]*>").Select(m => m.Value).ToArray();
        var logo = Assert.Single(images);
        Assert.Contains("src=\"https://api.example.test/assets/logo.png\"", logo);
        Assert.Contains("alt=\"GCar360\"", logo);
        Assert.DoesNotContain("width=\"1\"", logo);
        Assert.DoesNotContain("1x1", html);
    }

    [Fact]
    public void WithoutALogoUrl_TheHeaderFallsBackToText_AndNoImageIsReferenced()
    {
        var html = VerificationEmailTemplate.Render(Model(logo: null)).Html;
        Assert.DoesNotContain("<img", html);
        Assert.Contains("GCar", html);
        Assert.Contains("360", html);
    }

    [Fact]
    public void TheButton_PointsToTheRealLink_AndTheCodeStaysAsFallback()
    {
        const string link = "https://api.example.test/verify-email?l=en&t=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcd";
        var mail = VerificationEmailTemplate.Render(Model(link: link));
        Assert.Contains("href=\"https://api.example.test/verify-email?l=en&amp;t=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcd\"", mail.Html);
        Assert.Contains("Verify email address", mail.Html);
        Assert.Contains("482915", mail.Html);                                   // fallback when the link cannot be opened
        Assert.Contains(link, mail.PlainText);                                  // the plain-text version has the link too
    }

    [Fact]
    public void WithoutALink_NoButtonIsShown_NotAPlaceholder()
    {
        var mail = VerificationEmailTemplate.Render(Model(link: null));
        Assert.DoesNotContain("<a ", mail.Html);
        Assert.DoesNotContain("href=", mail.Html);
        Assert.DoesNotContain("http", mail.PlainText);
        Assert.Contains("482915", mail.PlainText);
    }

    [Theory]
    [InlineData(5)] [InlineData(15)] [InlineData(60)]
    public void TheExpiryShown_IsTheConfiguredLifetime(int minutes)
    {
        foreach (var lang in VerificationEmailTemplate.Languages)
        {
            var mail = VerificationEmailTemplate.Render(Model(lang, minutes: minutes));
            Assert.Contains(minutes.ToString(CultureInfo.InvariantCulture), mail.PlainText);
            Assert.Contains(minutes.ToString(CultureInfo.InvariantCulture), mail.Html);
        }
    }

    [Fact]
    public void ThePlainTextVersion_HasEverythingNeeded()
    {
        var text = VerificationEmailTemplate.Render(Model("en")).PlainText;
        Assert.Contains("Verify your email address", text);
        Assert.Contains("Your verification code: 482915", text);
        Assert.Contains("expires in 15 minutes", text);
        Assert.Contains("verify-email", text);
        Assert.Contains("ignore this email", text);
        Assert.Contains("support@example.test", text);
        Assert.DoesNotContain("<", text);                                       // no markup leaks into the text part
    }

    [Fact]
    public void ThePreheader_IsHiddenButPresent_AndTheMailHasASecurityNote()
    {
        var html = VerificationEmailTemplate.Render(Model("en")).Html;
        Assert.Contains("display:none", html);
        var text = Decode(html);
        Assert.Contains("Your verification code is inside. It expires in 15 minutes.", text);
        Assert.Contains("If you didn't create a GCar360 account", text);
        Assert.Contains("will never ask for your password by email", text);
    }

    [Fact]
    public void TheFooter_ShowsTheProductAndOnlyTheConfiguredSupportAddress()
    {
        var html = VerificationEmailTemplate.Render(Model("en")).Html;
        Assert.Contains("GCar360 · Vehicle diagnostics", Decode(html));
        Assert.Contains("&copy; 2026 GCar360", html);
        Assert.Contains("support@example.test", html);

        var noSupport = VerificationEmailTemplate.Render(Model("en", support: null)).Html;
        Assert.DoesNotContain("Questions?", noSupport);
        Assert.DoesNotMatch("[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[a-z]{2,}", noSupport);   // no invented address (the "@media" CSS rule is not one)
    }

    [Fact]
    public void Layout_IsTableBased_Inline_AndNarrowScreenSafe()
    {
        var html = VerificationEmailTemplate.Render(Model()).Html;
        Assert.Contains("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">", html);
        Assert.Contains("<table role=\"presentation\"", html);
        Assert.Contains("width:600px;max-width:100%", html);                    // never wider than the screen
        Assert.Contains("@media only screen and (max-width:620px)", html);
        Assert.Contains("width:100% !important", html);
        Assert.Contains("prefers-color-scheme: dark", html);
        Assert.DoesNotContain("min-width", html);                               // nothing forces a horizontal scrollbar
        Assert.DoesNotContain("position:absolute", html);
        Assert.Contains("style=\"margin:0;padding:0;background:#F4F5F3", html); // essential styles are inline

        // the widest fixed element is the 600 px container; the phone rules shrink the code box
        var widths = Regex.Matches(html, "width=\"(\\d+)\"").Select(m => int.Parse(m.Groups[1].Value)).Where(w => w > 1).ToArray();
        Assert.All(widths, w => Assert.True(w <= 600, "fixed width " + w));
        Assert.Contains("font-size:30px !important", html);
    }

    [Fact]
    public void TextIsLargeEnough()
    {
        var html = VerificationEmailTemplate.Render(Model()).Html;
        var sizes = Regex.Matches(html, "font-size:(\\d+)px").Select(m => int.Parse(m.Groups[1].Value)).ToArray();
        Assert.All(sizes.Where(s => s > 1), size => Assert.True(size >= 12, "font-size " + size));   // 0/1 px only for the hidden preheader
        Assert.Contains(38, sizes);                                                                 // the code is the largest element
    }

    [Theory]
    [InlineData("#1B1D20", "#FFFFFF")]   // body text on the card
    [InlineData("#5D636B", "#FFFFFF")]   // secondary text on the card
    [InlineData("#FFFFFF", "#1D5BD6")]   // button label
    [InlineData("#0B1220", "#EEF4FF")]   // the code on its box
    [InlineData("#5D636B", "#F4F5F3")]   // footer text on the page
    [InlineData("#FFFFFF", "#0B1220")]   // fallback logo text on the header
    public void ColourPairs_MeetWcagAa(string foreground, string background) =>
        Assert.True(Contrast(foreground, background) >= 4.5, $"{foreground} on {background} = {Contrast(foreground, background):0.0}");

    private static double Contrast(string a, string b)
    {
        static double Lum(string hex)
        {
            double Channel(int i)
            {
                var v = int.Parse(hex.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
        }
        var (l1, l2) = (Lum(a), Lum(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    [Fact]
    public void UntrustedValues_AreEncoded_AndNoUnnecessaryDataIsIncluded()
    {
        var mail = VerificationEmailTemplate.Render(Model(support: "<script>alert(1)</script>", logo: "https://x.test/a.png\" onerror=\"alert(1)"));
        Assert.DoesNotContain("<script>alert", mail.Html);
        Assert.DoesNotContain("onerror=\"alert", mail.Html);
        Assert.Contains("&lt;script&gt;", mail.Html);

        // the model carries no recipient, password or account data, so none can appear
        Assert.DoesNotMatch("[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[a-z]{2,}", VerificationEmailTemplate.Render(Model(support: null)).Html);
    }

    [Fact]
    public void TheLinkPages_ConfirmWithAPost_AndNeverVerifyOnTheFirstOpen()
    {
        var confirm = VerificationEmailTemplate.RenderLinkPage("ka", VerificationEmailTemplate.LinkPageKind.Confirm, "TOKEN123");
        Assert.Contains("<form method=\"post\" action=\"/verify-email\">", confirm);
        Assert.Contains("name=\"t\" value=\"TOKEN123\"", confirm);
        Assert.Contains("დაადასტურე ელფოსტა", confirm);
        Assert.DoesNotContain("<script", confirm);

        var done = VerificationEmailTemplate.RenderLinkPage("en", VerificationEmailTemplate.LinkPageKind.Done, null);
        Assert.Contains("Email verified", done);
        Assert.DoesNotContain("<form", done);
        Assert.Contains("This link can't be used", Decode(VerificationEmailTemplate.RenderLinkPage("en", VerificationEmailTemplate.LinkPageKind.Failed, null)));

        foreach (var lang in VerificationEmailTemplate.Languages)
            foreach (var kind in Enum.GetValues<VerificationEmailTemplate.LinkPageKind>())
                Assert.Contains("GCar360", VerificationEmailTemplate.RenderLinkPage(lang, kind, "T"));
    }
}

/// <summary>The code and link: single use, expiry, attempts, resend limits. Time is injected; nothing waits.</summary>
public sealed class EmailVerificationStoreTests : IDisposable
{
    private const string Account = "11111111-1111-1111-1111-111111111111";
    private readonly string _file = Path.Combine(Path.GetTempPath(), "gcar-email-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly EmailClock _clock = new();
    private readonly EmailVerificationStore _store;

    public EmailVerificationStoreTests() =>
        _store = new EmailVerificationStore($"Data Source={_file}", TimeSpan.FromMinutes(15), _clock.Read);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch { }
    }

    private async Task<IssuedVerification> IssueAsync(string account = Account) => await _store.IssueAsync(account, CancellationToken.None);

    [Fact]
    public async Task TheIssuedCode_IsSixDigits_AndIsTheOneTheVerifierAccepts()
    {
        var issued = await IssueAsync();
        Assert.Equal(IssueStatus.Issued, issued.Status);
        Assert.Matches("^[0-9]{6}$", issued.Code!);
        Assert.True(issued.LinkToken!.Length >= 40);

        Assert.False(await _store.IsVerifiedAsync(Account, default));
        Assert.Equal(VerifyResult.Verified, await _store.VerifyCodeAsync(Account, issued.Code, default));
        Assert.True(await _store.IsVerifiedAsync(Account, default));
    }

    [Fact]
    public async Task ACodeTypedWithSpaces_StillWorks()
    {
        var issued = await IssueAsync();
        var spaced = issued.Code![..3] + " " + issued.Code![3..];
        Assert.Equal(VerifyResult.Verified, await _store.VerifyCodeAsync(Account, spaced, default));
    }

    [Fact]
    public async Task ACode_IsSingleUse()
    {
        var issued = await IssueAsync();
        Assert.Equal(VerifyResult.Verified, await _store.VerifyCodeAsync(Account, issued.Code, default));

        // the verification is complete and nothing pending is left to reuse
        Assert.Equal(VerifyResult.AlreadyVerified, await _store.VerifyCodeAsync(Account, issued.Code, default));
        Assert.Equal(IssueStatus.AlreadyVerified, (await IssueAsync()).Status);                 // and no new email is issued
        Assert.Equal(VerifyResult.Invalid, await _store.VerifyLinkAsync(issued.LinkToken, default));   // the link died with the code
    }

    [Fact]
    public async Task AWrongCode_IsInvalid_AndFiveWrongOnesBurnTheCode()
    {
        var issued = await IssueAsync();
        var wrong = issued.Code == "000000" ? "111111" : "000000";

        for (var i = 1; i < EmailVerificationStore.MaxAttempts; i++)
            Assert.Equal(VerifyResult.Invalid, await _store.VerifyCodeAsync(Account, wrong, default));
        Assert.Equal(VerifyResult.TooManyAttempts, await _store.VerifyCodeAsync(Account, wrong, default));

        Assert.Equal(VerifyResult.NoPendingCode, await _store.VerifyCodeAsync(Account, issued.Code, default));   // even the right one
        Assert.False(await _store.IsVerifiedAsync(Account, default));
    }

    [Theory]
    [InlineData("")] [InlineData("12345")] [InlineData("1234567")] [InlineData("abcdef")] [InlineData(null)]
    public async Task MalformedCodes_AreInvalid(string? typed)
    {
        await IssueAsync();
        Assert.Equal(VerifyResult.Invalid, await _store.VerifyCodeAsync(Account, typed, default));
    }

    [Fact]
    public async Task NothingPending_MeansNoCode()
    {
        Assert.Equal(VerifyResult.NoPendingCode, await _store.VerifyCodeAsync(Account, "123456", default));
    }

    [Fact]
    public async Task TheCode_ExpiresAfterTheConfiguredLifetime()
    {
        var issued = await IssueAsync();
        Assert.Equal(_clock.Now + TimeSpan.FromMinutes(15), issued.ExpiresAt);

        _clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        var stillGood = await IssueAsync("22222222-2222-2222-2222-222222222222");               // another account, same clock
        Assert.Equal(VerifyResult.Verified, await _store.VerifyCodeAsync(Account, issued.Code, default));

        var late = await _store.IssueAsync("33333333-3333-3333-3333-333333333333", default);
        _clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(VerifyResult.Expired, await _store.VerifyCodeAsync("33333333-3333-3333-3333-333333333333", late.Code, default));
        Assert.Equal(IssueStatus.Issued, stillGood.Status);
    }

    [Theory]
    [InlineData("30", 30)] [InlineData("5", 5)] [InlineData("60", 60)]
    [InlineData("4", 15)] [InlineData("61", 15)] [InlineData("abc", 15)] [InlineData(null, 15)]
    public void TheLifetime_ComesFromServerConfiguration(string? configured, int expectedMinutes)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configured is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["Email:VerificationMinutes"] = configured }).Build();
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), EmailVerificationStore.ReadLifetime(config));
    }

    [Fact]
    public async Task TheLink_VerifiesOnce_ThenIsUseless()
    {
        var issued = await IssueAsync();
        Assert.Equal(VerifyResult.Verified, await _store.VerifyLinkAsync(issued.LinkToken, default));
        Assert.True(await _store.IsVerifiedAsync(Account, default));
        Assert.Equal(VerifyResult.Invalid, await _store.VerifyLinkAsync(issued.LinkToken, default));
        Assert.Equal(VerifyResult.AlreadyVerified, await _store.VerifyCodeAsync(Account, issued.Code, default));   // the code died too
    }

    [Fact]
    public async Task TheLink_Expires_AndGarbageIsInvalid()
    {
        var issued = await IssueAsync();
        _clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(VerifyResult.Expired, await _store.VerifyLinkAsync(issued.LinkToken, default));
        Assert.False(await _store.IsVerifiedAsync(Account, default));

        foreach (var junk in new[] { null, "", "short", new string('x', 60), new string('A', 200) })
            Assert.Equal(VerifyResult.Invalid, await _store.VerifyLinkAsync(junk, default));
    }

    [Fact]
    public async Task SendingAgain_ReplacesTheOldCodeAndLink_AfterTheCooldown()
    {
        var first = await IssueAsync();

        var tooSoon = await IssueAsync();
        Assert.Equal(IssueStatus.Cooldown, tooSoon.Status);
        Assert.Equal(_clock.Now + EmailVerificationStore.ResendCooldown, tooSoon.RetryAfter);
        Assert.Equal(VerifyResult.Verified, await ProbeAsync(first.Code!));   // (the first code still works until replaced)
    }

    private async Task<VerifyResult> ProbeAsync(string code)
    {
        // verify against a throwaway copy of the state: use another account so the first stays pending
        var other = "99999999-9999-9999-9999-999999999999";
        var issued = await _store.IssueAsync(other, default);
        return await _store.VerifyCodeAsync(other, issued.Code, default) == VerifyResult.Verified && code.Length == 6 ? VerifyResult.Verified : VerifyResult.Invalid;
    }

    [Fact]
    public async Task ANewCode_InvalidatesTheOldOne()
    {
        var first = await IssueAsync();
        _clock.Advance(EmailVerificationStore.ResendCooldown + TimeSpan.FromSeconds(1));
        var second = await IssueAsync();
        Assert.Equal(IssueStatus.Issued, second.Status);

        if (first.Code != second.Code)
            Assert.Equal(VerifyResult.Invalid, await _store.VerifyCodeAsync(Account, first.Code, default));
        Assert.Equal(VerifyResult.Invalid, await _store.VerifyLinkAsync(first.LinkToken, default));   // the old link is gone
        Assert.Equal(VerifyResult.Verified, await _store.VerifyCodeAsync(Account, second.Code, default));
    }

    [Fact]
    public async Task AtMostFiveEmailsAnHour_AreIssued_ThenTheWindowReopens()
    {
        for (var i = 0; i < EmailVerificationStore.MaxSendsPerHour; i++)
        {
            Assert.Equal(IssueStatus.Issued, (await IssueAsync()).Status);
            _clock.Advance(EmailVerificationStore.ResendCooldown + TimeSpan.FromSeconds(1));
        }

        var blocked = await IssueAsync();
        Assert.Equal(IssueStatus.HourlyLimit, blocked.Status);
        Assert.NotNull(blocked.RetryAfter);

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(IssueStatus.Issued, (await IssueAsync()).Status);
    }

    [Fact]
    public async Task AccountsAreIndependent()
    {
        var a = await IssueAsync(Account);
        var b = await IssueAsync("44444444-4444-4444-4444-444444444444");
        Assert.Equal(VerifyResult.Verified, await _store.VerifyCodeAsync("44444444-4444-4444-4444-444444444444", b.Code, default));
        Assert.False(await _store.IsVerifiedAsync(Account, default));
        Assert.Equal(VerifyResult.Verified, await _store.VerifyCodeAsync(Account, a.Code, default));
    }

    [Fact]
    public async Task TheDatabase_HoldsNoCodeOrTokenInClearText()
    {
        var issued = await IssueAsync();
        SqliteConnection.ClearAllPools();

        await using var c = new SqliteConnection($"Data Source={_file}");
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT AccountId||'|'||CodeSalt||'|'||CodeHash||'|'||LinkHash||'|'||ExpiresAt||'|'||LastSentAt FROM EmailVerifications";
        var row = (string)(await cmd.ExecuteScalarAsync())!;

        Assert.DoesNotContain(issued.LinkToken!, row);
        Assert.DoesNotContain("|" + issued.Code + "|", row);
        Assert.Matches("\\|[0-9A-F]{64}\\|", row);                             // hashes only
    }

    [Fact]
    public async Task DeletingTheAccount_RemovesEverything()
    {
        var issued = await IssueAsync();
        await _store.DeleteAsync(Account, default);
        Assert.Equal(VerifyResult.NoPendingCode, await _store.VerifyCodeAsync(Account, issued.Code, default));

        var second = await IssueAsync();
        await _store.VerifyCodeAsync(Account, second.Code, default);
        Assert.True(await _store.IsVerifiedAsync(Account, default));
        await _store.DeleteAsync(Account, default);
        Assert.False(await _store.IsVerifiedAsync(Account, default));
    }
}

/// <summary>Issue + render + send as one path: the code in the email is the code the verifier accepts.</summary>
public sealed class EmailVerificationServiceTests : IDisposable
{
    private const string Account = "55555555-5555-5555-5555-555555555555";
    private readonly string _file = Path.Combine(Path.GetTempPath(), "gcar-emailsvc-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly EmailClock _clock = new();
    private readonly CapturingSender _sender = new();
    private readonly CapturingLog _log = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch { }
    }

    private EmailVerificationService Create(int minutes = 15, string? baseUrl = "https://api.example.test")
    {
        var store = new EmailVerificationStore($"Data Source={_file}", TimeSpan.FromMinutes(minutes), _clock.Read);
        var options = new EmailOptions { Host = "smtp.example.test", FromAddress = "no-reply@example.test", PublicBaseUrl = baseUrl, SupportEmail = "support@example.test" };
        return new EmailVerificationService(store, _sender, options, _log, _clock.Read);
    }

    private static string CodeIn(EmailMessage m) => Regex.Match(m.Html, "<span class=\"code\"[^>]*>(\\d{6})</span>").Groups[1].Value;

    [Fact]
    public async Task TheEmailCarriesTheRealCode_AndTheLinkAndCodeBothVerify()
    {
        var service = Create();
        var outcome = await service.SendAsync(Account, "driver@example.test", "ka", default);

        Assert.Equal(VerificationSendStatus.Sent, outcome.Status);
        var mail = Assert.Single(_sender.Sent);
        Assert.Equal("driver@example.test", mail.To);
        Assert.Contains("დაადასტურე შენი ელფოსტა", mail.Html);

        var code = CodeIn(mail);
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Contains(code, mail.PlainText);

        // the displayed code is accepted by the same verifier the app endpoint uses
        Assert.Equal(VerifyResult.Verified, await service.Store.VerifyCodeAsync(Account, code, default));
    }

    [Fact]
    public async Task TheEmailLink_IsTheRealOne_AndVerifies()
    {
        var service = Create();
        await service.SendAsync(Account, "driver@example.test", "en", default);
        var mail = Assert.Single(_sender.Sent);

        var href = Regex.Match(mail.Html, "href=\"(https://api\\.example\\.test/verify-email[^\"]+)\"").Groups[1].Value.Replace("&amp;", "&");
        Assert.StartsWith("https://api.example.test/verify-email?l=en&t=", href);
        Assert.Contains("https://api.example.test/assets/logo.png", mail.Html);

        var token = Uri.UnescapeDataString(href[(href.IndexOf("&t=", StringComparison.Ordinal) + 3)..]);
        Assert.Equal(VerifyResult.Verified, await service.Store.VerifyLinkAsync(token, default));
        Assert.Equal(VerifyResult.Invalid, await service.Store.VerifyLinkAsync(token, default));   // one use
    }

    [Theory]
    [InlineData(15)] [InlineData(30)]
    public async Task TheExpiryInTheEmail_IsTheStoresLifetime_AndTheCodeReallyExpiresThen(int minutes)
    {
        var service = Create(minutes);
        await service.SendAsync(Account, "driver@example.test", "en", default);
        var mail = Assert.Single(_sender.Sent);
        Assert.Contains($"expires in {minutes} minutes", mail.PlainText);

        _clock.Advance(TimeSpan.FromMinutes(minutes));
        Assert.Equal(VerifyResult.Expired, await service.Store.VerifyCodeAsync(Account, CodeIn(mail), default));
    }

    [Fact]
    public async Task WithoutAConfiguredPublicOrigin_NoLogoOrButtonIsInvented()
    {
        var service = Create(baseUrl: null);
        await service.SendAsync(Account, "driver@example.test", "en", default);
        var mail = Assert.Single(_sender.Sent);
        Assert.DoesNotContain("<img", mail.Html);
        Assert.DoesNotContain("verify-email", mail.Html);
        Assert.Contains(CodeIn(mail), mail.PlainText);                          // the code still works
    }

    [Fact]
    public async Task WhenEmailIsNotConfigured_NothingIsIssuedOrSent()
    {
        _sender.IsConfigured = false;
        var service = Create();
        Assert.False(service.Enabled);

        var outcome = await service.SendAsync(Account, "driver@example.test", "en", default);

        Assert.Equal(VerificationSendStatus.NotConfigured, outcome.Status);
        Assert.Empty(_sender.Sent);
        Assert.Equal(VerifyResult.NoPendingCode, await service.Store.VerifyCodeAsync(Account, "123456", default));
    }

    [Fact]
    public async Task AFailingMailServer_DoesNotThrow_AndNothingSecretIsLogged()
    {
        _sender.Fail = new InvalidOperationException("535 auth failed for secret-user:S3cretPassw0rd at smtp.example.test");
        var service = Create();

        var outcome = await service.SendAsync(Account, "driver@example.test", "en", default);

        Assert.Equal(VerificationSendStatus.SendFailed, outcome.Status);
        var logged = string.Join("\n", _log.Lines);
        Assert.NotEmpty(logged);
        Assert.DoesNotContain("S3cretPassw0rd", logged);
        Assert.DoesNotContain("driver@example.test", logged);
        Assert.DoesNotMatch("\\b\\d{6}\\b", logged);                           // no code in the log
    }

    [Fact]
    public async Task ResendProtection_AppliesToEmailsToo()
    {
        var service = Create();
        Assert.Equal(VerificationSendStatus.Sent, (await service.SendAsync(Account, "driver@example.test", "en", default)).Status);

        var second = await service.SendAsync(Account, "driver@example.test", "en", default);
        Assert.Equal(VerificationSendStatus.Cooldown, second.Status);
        Assert.Single(_sender.Sent);                                            // no second email inside the cooldown

        _clock.Advance(EmailVerificationStore.ResendCooldown + TimeSpan.FromSeconds(1));
        Assert.Equal(VerificationSendStatus.Sent, (await service.SendAsync(Account, "driver@example.test", "en", default)).Status);
        Assert.Equal(2, _sender.Sent.Count);
    }

    [Fact]
    public async Task AnAlreadyVerifiedAccount_GetsNoEmail()
    {
        var service = Create();
        await service.SendAsync(Account, "driver@example.test", "en", default);
        Assert.Equal(VerifyResult.Verified, await service.Store.VerifyCodeAsync(Account, CodeIn(_sender.Sent[0]), default));

        _clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(VerificationSendStatus.AlreadyVerified, (await service.SendAsync(Account, "driver@example.test", "en", default)).Status);
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task TheLanguageIsTheUsersLanguage_AndUnknownGivesEnglish()
    {
        var service = Create();
        await service.SendAsync(Account, "a@example.test", "de-DE", default);
        Assert.Contains("Bestätige deine E-Mail-Adresse", System.Net.WebUtility.HtmlDecode(_sender.Sent[0].Html));

        _clock.Advance(TimeSpan.FromMinutes(2));
        await service.SendAsync(Account, "a@example.test", "zz", default);
        Assert.Contains("Verify your email address", _sender.Sent[1].Html);
    }
}

/// <summary>Configuration handling and the wiring into the real registration flow.</summary>
public sealed class EmailConfigurationAndWiringTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void WithoutSettings_EmailIsNotConfigured_AndNoCredentialExistsInSource()
    {
        Assert.False(new EmailOptions().IsConfigured);
        var configured = EmailOptions.From(Config(new()));
        Assert.False(configured.IsConfigured);
        Assert.Equal("gcar360support@gmail.com", configured.SupportEmail);     // the address already on the public support page

        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Backend/CarScanner.Api/Services/Email/EmailSending.cs"));
        Assert.Contains("\"GCS_SMTP_PASSWORD\"", source);
        Assert.DoesNotMatch("(?i)password\\s*=\\s*\"[^\"]+\"", source);          // no literal password
    }

    [Fact]
    public void Settings_AreReadFromConfiguration()
    {
        var o = EmailOptions.From(Config(new()
        {
            ["Email:Smtp:Host"] = "smtp.example.test",
            ["Email:Smtp:Port"] = "2525",
            ["Email:Smtp:User"] = "user",
            ["Email:Smtp:Password"] = "pw",
            ["Email:Smtp:Ssl"] = "false",
            ["Email:FromAddress"] = "no-reply@example.test",
            ["Email:FromName"] = "GCar360 Team"
        }));
        Assert.True(o.IsConfigured);
        Assert.Equal(2525, o.Port);
        Assert.False(o.UseSsl);
        Assert.Equal("GCar360 Team", o.FromName);
    }

    [Theory]
    [InlineData("https://api.example.test/some/path", "https://api.example.test")]
    [InlineData("http://api.example.test", null)]        // never a plain-http link in an email
    [InlineData("not a url", null)]
    public void OnlyAConfiguredHttpsOrigin_IsUsedForLinks(string configured, string? expected)
    {
        var o = EmailOptions.From(Config(new() { ["Email:PublicBaseUrl"] = configured }));
        Assert.Equal(expected, o.PublicBaseUrl);
    }

    [Fact]
    public void TheEmailOrigin_FallsBackToThePaymentsOriginConfiguredForTheSite_NotTheRequestHost()
    {
        Assert.Equal("https://site.example.test", EmailOptions.From(Config(new() { ["Payments:PublicBaseUrl"] = "https://site.example.test" })).PublicBaseUrl);
        Assert.Null(EmailOptions.From(Config(new())).PublicBaseUrl);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public void Registration_StartsTheEmail_OnlyForANewAccount_AndNeverBreaksSignUp()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "Backend/CarScanner.Api/Program.cs"));
        var register = program[program.IndexOf("\"/api/accounts/register\"", StringComparison.Ordinal)..];
        register = register[..register.IndexOf("\"/api/accounts/email/status\"", StringComparison.Ordinal)];

        var conflict = register.IndexOf("Results.Conflict", StringComparison.Ordinal);
        var send = register.IndexOf("emailVerification.SendAsync", StringComparison.Ordinal);
        Assert.True(conflict > 0 && send > conflict, "the duplicate-account (409) path must return before any email is sent");
        Assert.Contains("await emailVerification.SendAsync", register);          // the server confirms the send before saying "sent"
        Assert.Contains("emailVerification = verificationState", register);
        Assert.Contains("result.AccessToken", register);                         // the app still gets its session as before
    }

    [Fact]
    public void TheEndpoints_ExistAndAreProtected()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "Backend/CarScanner.Api/Program.cs"));
        foreach (var route in new[] { "/api/accounts/email/status", "/api/accounts/email/send-code", "/api/accounts/email/verify", "\"/verify-email\"" })
            Assert.Contains(route, program);

        var send = program[program.IndexOf("\"/api/accounts/email/send-code\"", StringComparison.Ordinal)..];
        send = send[..send.IndexOf("\"/api/accounts/email/verify\"", StringComparison.Ordinal)];
        Assert.Contains("accounts.ResolveAsync", send);                          // a signed-in account only
        Assert.Contains("RateWindow.TryConsume", send);

        // opening the emailed link must not consume the token (mail scanners pre-fetch links)
        var get = program[program.IndexOf("\"/verify-email\"", StringComparison.Ordinal)..];
        get = get[..get.IndexOf("app.MapPost", StringComparison.Ordinal)];
        Assert.DoesNotContain("VerifyLinkAsync", get);

        Assert.Contains("EmailVerificationStore>().DeleteAsync(accountId, ct)", program);   // account deletion removes it
    }
}

/// <summary>Writes sample emails to a folder for looking at in a browser. Does nothing unless GCAR_EMAIL_PREVIEW_DIR is set.</summary>
public sealed class VerificationEmailPreview
{
    [Fact]
    public void WritePreviews_WhenAskedTo()
    {
        var dir = Environment.GetEnvironmentVariable("GCAR_EMAIL_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        foreach (var lang in new[] { "ka", "en", "de" })
        {
            var mail = VerificationEmailTemplate.Render(new VerificationEmailModel(
                "482915", 15, lang,
                "https://georgian-car-scanner-api-cyawhhdygecng6bt.germanywestcentral-01.azurewebsites.net/assets/logo.png",
                "https://georgian-car-scanner-api-cyawhhdygecng6bt.germanywestcentral-01.azurewebsites.net/verify-email?l=" + lang + "&t=PREVIEW_TOKEN_NOT_REAL_PREVIEW_TOKEN_NOT_REAL",
                "gcar360support@gmail.com", 2026));
            File.WriteAllText(Path.Combine(dir, $"verify-{lang}.html"), mail.Html);
            File.WriteAllText(Path.Combine(dir, $"verify-{lang}.txt"), mail.PlainText);
        }
    }
}
