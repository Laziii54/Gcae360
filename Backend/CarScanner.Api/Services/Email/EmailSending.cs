using System.Net;
using System.Net.Mail;
using System.Text;

namespace CarScanner.Api.Services.Email;

public sealed record EmailMessage(string To, string Subject, string Html, string PlainText);

public interface IEmailSender
{
    /// <summary>False until the SMTP settings are present on the server. Nothing is sent while false.</summary>
    bool IsConfigured { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>
/// Email settings, all from server configuration (environment variables or appsettings), never from source:
///   GCS_SMTP_HOST, GCS_SMTP_PORT (587), GCS_SMTP_USER, GCS_SMTP_PASSWORD, GCS_SMTP_SSL (true),
///   GCS_EMAIL_FROM (sender address), GCS_EMAIL_FROM_NAME ("GCar360"),
///   GCS_EMAIL_CODE_MINUTES (15), GCS_PUBLIC_BASE_URL (https origin for the logo and the email button),
///   GCS_SUPPORT_EMAIL (the support address shown in the footer).
/// The same keys exist under "Email:" in appsettings (Email:Smtp:Host, …).
/// </summary>
public sealed class EmailOptions
{
    /// <summary>The support address already published on the site's support page.</summary>
    public const string DefaultSupportEmail = "gcar360support@gmail.com";

    public string? Host { get; init; }
    public int Port { get; init; } = 587;
    public string? User { get; init; }
    public string? Password { get; init; }
    public bool UseSsl { get; init; } = true;
    public string? FromAddress { get; init; }
    public string FromName { get; init; } = "GCar360";
    public string? PublicBaseUrl { get; init; }
    public string SupportEmail { get; init; } = DefaultSupportEmail;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);

    public static EmailOptions From(IConfiguration config)
    {
        static string? Pick(IConfiguration c, string key, string env) =>
            FirstNonEmpty(c[key], Environment.GetEnvironmentVariable(env));

        var baseUrl = FirstNonEmpty(Pick(config, "Email:PublicBaseUrl", "GCS_PUBLIC_BASE_URL"), config["Payments:PublicBaseUrl"]);
        // Only a configured https origin is used for links in emails: never the request's Host header (it can be forged).
        string? origin = null;
        if (baseUrl is not null && Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            origin = uri.GetLeftPart(UriPartial.Authority);

        return new EmailOptions
        {
            Host = Pick(config, "Email:Smtp:Host", "GCS_SMTP_HOST"),
            Port = int.TryParse(Pick(config, "Email:Smtp:Port", "GCS_SMTP_PORT"), out var port) && port is > 0 and < 65536 ? port : 587,
            User = Pick(config, "Email:Smtp:User", "GCS_SMTP_USER"),
            Password = Pick(config, "Email:Smtp:Password", "GCS_SMTP_PASSWORD"),
            UseSsl = !string.Equals(Pick(config, "Email:Smtp:Ssl", "GCS_SMTP_SSL"), "false", StringComparison.OrdinalIgnoreCase),
            FromAddress = Pick(config, "Email:FromAddress", "GCS_EMAIL_FROM"),
            FromName = Pick(config, "Email:FromName", "GCS_EMAIL_FROM_NAME") ?? "GCar360",
            PublicBaseUrl = origin,
            SupportEmail = Pick(config, "Email:SupportEmail", "GCS_SUPPORT_EMAIL") ?? DefaultSupportEmail
        };
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}

/// <summary>Sends through any standard SMTP relay (the credentials stay in server settings). Multipart: plain text and HTML.</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(EmailOptions options) => _options = options;

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Email is not configured.");

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress!, _options.FromName, Encoding.UTF8),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
            Body = message.PlainText
        };
        mail.To.Add(new MailAddress(message.To));
        mail.Headers.Add("Auto-Submitted", "auto-generated");
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.PlainText, Encoding.UTF8, "text/plain"));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.Html, Encoding.UTF8, "text/html"));

        using var client = new SmtpClient(_options.Host!, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 20_000
        };
        if (!string.IsNullOrWhiteSpace(_options.User))
            client.Credentials = new NetworkCredential(_options.User, _options.Password ?? string.Empty);

        await client.SendMailAsync(mail, ct);
    }
}

public enum VerificationSendStatus
{
    Sent,
    NotConfigured,
    AlreadyVerified,
    Cooldown,
    HourlyLimit,
    SendFailed
}

public sealed record VerificationSendOutcome(VerificationSendStatus Status, DateTimeOffset? RetryAfter = null);

/// <summary>
/// Issues the real verification (code and link) and sends the branded email that carries exactly those values.
/// Used by registration and by the "send a new code" endpoint, so there is one path and no email-only code.
/// </summary>
public sealed class EmailVerificationService
{
    private readonly EmailVerificationStore _store;
    private readonly IEmailSender _sender;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailVerificationService> _log;
    private readonly Func<DateTimeOffset> _clock;

    public EmailVerificationService(EmailVerificationStore store, IEmailSender sender, EmailOptions options, ILogger<EmailVerificationService> log, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _sender = sender;
        _options = options;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool Enabled => _sender.IsConfigured;

    public EmailVerificationStore Store => _store;

    public async Task<VerificationSendOutcome> SendAsync(string accountId, string email, string? language, CancellationToken ct)
    {
        if (!Enabled)
            return new VerificationSendOutcome(VerificationSendStatus.NotConfigured);

        var issued = await _store.IssueAsync(accountId, ct);
        switch (issued.Status)
        {
            case IssueStatus.AlreadyVerified:
                return new VerificationSendOutcome(VerificationSendStatus.AlreadyVerified);
            case IssueStatus.Cooldown:
                return new VerificationSendOutcome(VerificationSendStatus.Cooldown, issued.RetryAfter);
            case IssueStatus.HourlyLimit:
                return new VerificationSendOutcome(VerificationSendStatus.HourlyLimit, issued.RetryAfter);
        }

        var lang = VerificationEmailTemplate.ResolveLanguage(language);
        var origin = _options.PublicBaseUrl;
        var model = new VerificationEmailModel(
            issued.Code!,
            (int)Math.Round(_store.Lifetime.TotalMinutes),
            lang,
            origin is null ? null : origin + "/assets/logo.png",
            origin is null ? null : $"{origin}/verify-email?l={lang}&t={Uri.EscapeDataString(issued.LinkToken!)}",
            _options.SupportEmail,
            _clock().Year);
        var rendered = VerificationEmailTemplate.Render(model);

        try
        {
            await _sender.SendAsync(new EmailMessage(email, rendered.Subject, rendered.Html, rendered.PlainText), ct);
            return new VerificationSendOutcome(VerificationSendStatus.Sent);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The type of failure only: never the address, the code or the SMTP credentials.
            _log.LogWarning("Verification email could not be sent ({ErrorType}).", ex.GetType().Name);
            try { await _store.MarkSendFailedAsync(accountId, CancellationToken.None); } catch { /* best effort */ }
            return new VerificationSendOutcome(VerificationSendStatus.SendFailed);
        }
    }
}
