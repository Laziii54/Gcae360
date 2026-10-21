namespace CarScanner.Api.Services.Email;

public enum LoginCodeSendStatus
{
    Sent,
    NotConfigured,
    Cooldown,
    HourlyLimit,
    SendFailed
}

public sealed record LoginCodeSendOutcome(LoginCodeSendStatus Status, DateTimeOffset? RetryAfter = null);

/// <summary>
/// Issues and emails the sign-in code required on every login. Reuses the same branded template and mail sender as
/// registration's <see cref="EmailVerificationService"/>, but never includes a confirmation link and never
/// remembers a past success — see <see cref="LoginCodeStore"/>.
/// </summary>
public sealed class LoginCodeService
{
    private readonly LoginCodeStore _store;
    private readonly IEmailSender _sender;
    private readonly EmailOptions _options;
    private readonly ILogger<LoginCodeService> _log;
    private readonly Func<DateTimeOffset> _clock;

    public LoginCodeService(LoginCodeStore store, IEmailSender sender, EmailOptions options, ILogger<LoginCodeService> log, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _sender = sender;
        _options = options;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool Enabled => _sender.IsConfigured;

    public LoginCodeStore Store => _store;

    public async Task<LoginCodeSendOutcome> SendAsync(string accountId, string email, string? language, CancellationToken ct)
    {
        if (!Enabled)
            return new LoginCodeSendOutcome(LoginCodeSendStatus.NotConfigured);

        var issued = await _store.IssueAsync(accountId, ct);
        switch (issued.Status)
        {
            case LoginCodeIssueStatus.Cooldown:
                return new LoginCodeSendOutcome(LoginCodeSendStatus.Cooldown, issued.RetryAfter);
            case LoginCodeIssueStatus.HourlyLimit:
                return new LoginCodeSendOutcome(LoginCodeSendStatus.HourlyLimit, issued.RetryAfter);
        }

        var lang = VerificationEmailTemplate.ResolveLanguage(language);
        var origin = _options.PublicBaseUrl;
        var model = new VerificationEmailModel(
            issued.Code!,
            (int)Math.Round(LoginCodeStore.Lifetime.TotalMinutes),
            lang,
            origin is null ? null : origin + "/assets/logo.png?v=2",
            null, // sign-in codes are entered in the app only — no confirmation link
            _options.SupportEmail,
            _clock().Year);
        var rendered = VerificationEmailTemplate.Render(model);

        try
        {
            await _sender.SendAsync(new EmailMessage(email, rendered.Subject, rendered.Html, rendered.PlainText), ct);
            return new LoginCodeSendOutcome(LoginCodeSendStatus.Sent);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The type of failure only: never the address, the code or the SMTP credentials.
            _log.LogWarning("Login code email could not be sent ({ErrorType}).", ex.GetType().Name);
            try { await _store.MarkSendFailedAsync(accountId, CancellationToken.None); } catch { /* best effort */ }
            return new LoginCodeSendOutcome(LoginCodeSendStatus.SendFailed);
        }
    }
}
