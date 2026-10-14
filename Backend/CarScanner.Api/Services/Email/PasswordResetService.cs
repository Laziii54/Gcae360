namespace CarScanner.Api.Services.Email;

public enum PasswordResetSendStatus
{
    Sent,
    NotConfigured,
    Cooldown,
    HourlyLimit,
    SendFailed
}

public sealed record PasswordResetSendOutcome(PasswordResetSendStatus Status, DateTimeOffset? RetryAfter = null);

/// <summary>
/// Issues and emails the "forgot password" code. Reuses the same branded template and mail sender as
/// registration's <see cref="EmailVerificationService"/> and login's <see cref="LoginCodeService"/>, but against
/// the separate <see cref="PasswordResetStore"/> so it never collides with an in-progress login 2FA code for the
/// same account.
/// </summary>
public sealed class PasswordResetService
{
    private readonly PasswordResetStore _store;
    private readonly IEmailSender _sender;
    private readonly EmailOptions _options;
    private readonly ILogger<PasswordResetService> _log;
    private readonly Func<DateTimeOffset> _clock;

    public PasswordResetService(PasswordResetStore store, IEmailSender sender, EmailOptions options, ILogger<PasswordResetService> log, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _sender = sender;
        _options = options;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool Enabled => _sender.IsConfigured;

    public PasswordResetStore Store => _store;

    public async Task<PasswordResetSendOutcome> SendAsync(string accountId, string email, string? language, CancellationToken ct)
    {
        if (!Enabled)
            return new PasswordResetSendOutcome(PasswordResetSendStatus.NotConfigured);

        var issued = await _store.IssueAsync(accountId, ct);
        switch (issued.Status)
        {
            case PasswordResetIssueStatus.Cooldown:
                return new PasswordResetSendOutcome(PasswordResetSendStatus.Cooldown, issued.RetryAfter);
            case PasswordResetIssueStatus.HourlyLimit:
                return new PasswordResetSendOutcome(PasswordResetSendStatus.HourlyLimit, issued.RetryAfter);
        }

        var lang = VerificationEmailTemplate.ResolveLanguage(language);
        var origin = _options.PublicBaseUrl;
        var model = new VerificationEmailModel(
            issued.Code!,
            (int)Math.Round(PasswordResetStore.Lifetime.TotalMinutes),
            lang,
            origin is null ? null : origin + "/assets/logo.png",
            null, // entered in the app only — no confirmation link
            _options.SupportEmail,
            _clock().Year);
        var rendered = VerificationEmailTemplate.Render(model);

        try
        {
            await _sender.SendAsync(new EmailMessage(email, rendered.Subject, rendered.Html, rendered.PlainText), ct);
            return new PasswordResetSendOutcome(PasswordResetSendStatus.Sent);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The type of failure only: never the address, the code or the SMTP credentials.
            _log.LogWarning("Password reset email could not be sent ({ErrorType}).", ex.GetType().Name);
            try { await _store.MarkSendFailedAsync(accountId, CancellationToken.None); } catch { /* best effort */ }
            return new PasswordResetSendOutcome(PasswordResetSendStatus.SendFailed);
        }
    }
}
