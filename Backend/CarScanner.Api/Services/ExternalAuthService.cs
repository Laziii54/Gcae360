using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CarScanner.Api.Services.Email;

namespace CarScanner.Api.Services;

public enum ExternalAuthStatus
{
    /// <summary>Signed in (existing linked account, or a brand-new one when <see cref="ExternalAuthOutcome.IsNewAccount"/>).</summary>
    SignedIn,
    /// <summary>The e-mail already belongs to a password account: the person must prove they control it (password + e-mailed code) to link.</summary>
    LinkRequired,
    /// <summary>A sign-in code was e-mailed to the existing account; call link again with it.</summary>
    NeedsCode,
    /// <summary>Wrong password / code, unusable or expired token, banned, e-mail missing or not verified by the provider.</summary>
    Rejected,
    NotConfigured,
    /// <summary>The account is bound to another phone; the person may confirm moving it here.</summary>
    DeviceBound,
    /// <summary>Moved to another phone less than 7 days ago, or code mails throttled.</summary>
    Cooldown,
    /// <summary>The provider identity is already linked to a different account.</summary>
    AlreadyLinked
}

public sealed record ExternalAuthOutcome(
    ExternalAuthStatus Status,
    AccountSessionResult? Session = null,
    bool IsNewAccount = false,
    string? LinkTicket = null,
    string? Email = null,
    DateTimeOffset? RetryAfter = null,
    string? Reason = null);

/// <summary>
/// Google / Apple sign-in on top of the existing account system. The provider only proves who the person is; the
/// device binding, session length, bans, e-mail verification and PRO entitlements are the ones every account already
/// has (PRO is tied to the account id and the store receipt, never to how someone signed in).
/// </summary>
public sealed class ExternalAuthService
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(10);
    private const int MaxTicketAttempts = 8;   // password + code round trips, with a few typos

    private readonly ExternalIdentityVerifier _verifier;
    private readonly AccountStore _accounts;
    private readonly EmailVerificationStore _verification;
    private readonly LoginCodeService _loginCodes;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, LinkTicket> _tickets = new();

    private sealed class LinkTicket
    {
        public required string Provider { get; init; }
        public required string Subject { get; init; }
        public required string AccountId { get; init; }
        public required string Email { get; init; }
        public required string DeviceId { get; init; }
        public required DateTimeOffset Expires { get; init; }
        public int Attempts;
    }

    public ExternalAuthService(
        ExternalIdentityVerifier verifier,
        AccountStore accounts,
        EmailVerificationStore verification,
        LoginCodeService loginCodes,
        Func<DateTimeOffset>? clock = null)
    {
        _verifier = verifier;
        _accounts = accounts;
        _verification = verification;
        _loginCodes = loginCodes;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsConfigured(string provider) => _verifier.IsConfigured(provider);

    public async Task<ExternalAuthOutcome> SignInAsync(
        string provider,
        string? idToken,
        string? nonce,
        string deviceId,
        bool moveDevice,
        CancellationToken ct)
    {
        var verified = await _verifier.VerifyAsync(provider, idToken, nonce, ct);
        if (verified.Status == ExternalVerifyStatus.NotConfigured)
            return new ExternalAuthOutcome(ExternalAuthStatus.NotConfigured);
        if (verified.Claims is not { } claims)
            return new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "invalid_token");

        try
        {
            // 1) Already linked: a normal device-bound sign-in of that account.
            var linked = await _accounts.FindByExternalIdentityAsync(provider, claims.Subject, ct);
            if (linked is not null)
            {
                var session = await _accounts.SignInVerifiedAccountAsync(linked.AccountId, deviceId, moveDevice, ct);
                if (session is null)
                    return new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "unavailable");
                _verifier.MarkUsed(claims);
                return new ExternalAuthOutcome(ExternalAuthStatus.SignedIn, session);
            }

            // 2) First time with this provider identity. The address must come from the SIGNED token and be vouched
            //    for by the provider: nothing the app sends beside the token is trusted.
            if (claims.Email is null)
                return new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "email_missing");
            if (!claims.EmailVerified)
                return new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "email_not_verified");
            if (!CarScanner.Services.EmailAddressRules.IsValidForRegistration(claims.Email))
                return new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "email_invalid");

            // 3) The same e-mail already has a password account: never merge on e-mail alone. The person has to prove
            //    they control that account first.
            var existing = await _accounts.FindByEmailAsync(claims.Email, ct);
            if (existing is not null)
            {
                _verifier.MarkUsed(claims);
                var ticket = IssueTicket(provider, claims.Subject, existing, deviceId);
                return new ExternalAuthOutcome(ExternalAuthStatus.LinkRequired, LinkTicket: ticket, Email: existing.Email);
            }

            // 4) A new account for a new person: no form, no password they must invent.
            var created = await _accounts.CreateExternalAccountAsync(provider, claims.Subject, claims.Email, deviceId, ct);
            if (created is null)
            {
                // Lost a race with a parallel first sign-in (same identity or same e-mail): the retry takes path 1 or 3.
                return new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "retry");
            }

            _verifier.MarkUsed(claims);
            await _verification.MarkVerifiedAsync(created.AccountId, ct);
            return new ExternalAuthOutcome(ExternalAuthStatus.SignedIn, created, IsNewAccount: true);
        }
        catch (AccountDeviceMismatchException)
        {
            return new ExternalAuthOutcome(ExternalAuthStatus.DeviceBound);
        }
        catch (AccountRebindCooldownException cooldown)
        {
            return new ExternalAuthOutcome(ExternalAuthStatus.Cooldown, RetryAfter: cooldown.NextAllowedAt);
        }
        catch (ArgumentException)
        {
            return new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "invalid_request");
        }
    }

    /// <summary>
    /// Links the provider identity from a link ticket to the existing account, after the person proved control of it:
    /// the account password and, when e-mail is configured, the code just e-mailed to that account. The e-mail address
    /// comes from the ticket, never from the caller.
    /// </summary>
    public async Task<ExternalAuthOutcome> LinkAsync(
        string? ticketValue,
        string? password,
        string? code,
        string deviceId,
        bool moveDevice,
        string? language,
        CancellationToken ct)
    {
        var key = TicketKey(ticketValue);
        if (key is null || !_tickets.TryGetValue(key, out var ticket))
            return new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "ticket");

        var now = _clock();
        if (ticket.Expires <= now || !string.Equals(ticket.DeviceId.Trim(), deviceId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _tickets.TryRemove(key, out _);
            return new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "ticket");
        }

        if (Interlocked.Increment(ref ticket.Attempts) > MaxTicketAttempts)
        {
            _tickets.TryRemove(key, out _);
            return new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "ticket");
        }

        var identity = await _accounts.VerifyCredentialsAsync(ticket.Email, password ?? string.Empty, ct);
        if (identity is null || identity.AccountId != ticket.AccountId)
            return new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "credentials");

        if (_loginCodes.Enabled)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                var sent = await _loginCodes.SendAsync(identity.AccountId, identity.Email, language, ct);
                return sent.Status switch
                {
                    LoginCodeSendStatus.Sent => new ExternalAuthOutcome(ExternalAuthStatus.NeedsCode),
                    LoginCodeSendStatus.Cooldown or LoginCodeSendStatus.HourlyLimit =>
                        new ExternalAuthOutcome(ExternalAuthStatus.Cooldown, RetryAfter: sent.RetryAfter),
                    // Mail could not be sent right now: same fallback as the password login, a mail outage never locks anyone out.
                    _ => await FinishLinkAsync(key, ticket, identity, deviceId, moveDevice, ct)
                };
            }

            var verified = await _loginCodes.Store.VerifyAsync(identity.AccountId, code, ct);
            if (verified != LoginCodeVerifyResult.Verified)
                return new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "code");
        }

        return await FinishLinkAsync(key, ticket, identity, deviceId, moveDevice, ct);
    }

    private async Task<ExternalAuthOutcome> FinishLinkAsync(
        string key,
        LinkTicket ticket,
        AccountIdentity identity,
        string deviceId,
        bool moveDevice,
        CancellationToken ct)
    {
        var existingLink = await _accounts.FindByExternalIdentityAsync(ticket.Provider, ticket.Subject, ct);
        if (existingLink is null)
        {
            if (!await _accounts.LinkExternalIdentityAsync(identity.AccountId, ticket.Provider, ticket.Subject, ticket.Email, ct))
            {
                _tickets.TryRemove(key, out _);
                return new ExternalAuthOutcome(ExternalAuthStatus.AlreadyLinked);
            }
        }
        else if (existingLink.AccountId != identity.AccountId)
        {
            _tickets.TryRemove(key, out _);
            return new ExternalAuthOutcome(ExternalAuthStatus.AlreadyLinked);
        }

        _tickets.TryRemove(key, out _);

        try
        {
            var session = await _accounts.SignInVerifiedAccountAsync(identity.AccountId, deviceId, moveDevice, ct);
            return session is null
                ? new ExternalAuthOutcome(ExternalAuthStatus.Rejected, Reason: "unavailable")
                : new ExternalAuthOutcome(ExternalAuthStatus.SignedIn, session);
        }
        catch (AccountDeviceMismatchException)
        {
            // Linked now; the next provider sign-in (with "move to this phone") completes it.
            return new ExternalAuthOutcome(ExternalAuthStatus.DeviceBound);
        }
        catch (AccountRebindCooldownException cooldown)
        {
            return new ExternalAuthOutcome(ExternalAuthStatus.Cooldown, RetryAfter: cooldown.NextAllowedAt);
        }
    }

    private string IssueTicket(string provider, string subject, AccountIdentity account, string deviceId)
    {
        var now = _clock();
        if (_tickets.Count > 500)
        {
            foreach (var pair in _tickets)
            {
                if (pair.Value.Expires <= now)
                    _tickets.TryRemove(pair.Key, out _);
            }
        }

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        _tickets[TicketKey(raw)!] = new LinkTicket
        {
            Provider = provider,
            Subject = subject,
            AccountId = account.AccountId,
            Email = account.Email,
            DeviceId = deviceId,
            Expires = now + TicketLifetime
        };
        return raw;
    }

    /// <summary>Tickets are stored by hash, so a memory dump does not hand out usable tickets.</summary>
    private static string? TicketKey(string? raw) =>
        string.IsNullOrWhiteSpace(raw) || raw.Length > 128
            ? null
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
