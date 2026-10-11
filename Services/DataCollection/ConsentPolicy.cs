namespace CarScanner.Services.DataCollection;

public enum ConsentState
{
    /// <summary>Nothing decided yet (or a decline older than 24 hours): the question is open.</summary>
    NoDecision,

    /// <summary>Declined less than 24 hours ago: not asked again yet.</summary>
    DeclinedWaiting,

    /// <summary>Consent given. Automatic collection may run.</summary>
    Granted,

    /// <summary>Consent was given and then withdrawn. Collection stays off; the user can turn it on in Settings.</summary>
    Revoked
}

/// <summary>Small key/value store (Preferences in the app, a dictionary in tests).</summary>
public interface IConsentStorage
{
    bool GetBool(string key, bool fallback);
    void SetBool(string key, bool value);
    long GetLong(string key, long fallback);
    void SetLong(string key, long value);
}

/// <summary>
/// The single owner of the data-collection consent decision. The "granted" flag is the same preference the
/// collection pipeline has always read, so there is one switch and no second consent system. Everything else
/// (when it was given, declined or withdrawn) is kept as UTC timestamps, never as an in-memory timer, so the
/// 24-hour wait survives restarts and reboots.
///
///  • Granted is permanent until the user withdraws it in Settings (or the app's data is cleared).
///  • A decline is never consent; the question returns once 24 hours have passed.
///  • Withdrawing is an explicit choice: the first-run question is not asked again after it.
/// </summary>
public sealed class ConsentPolicy
{
    public const string GrantedKey = "data_collection_consent_v1";
    public const string GrantedAtKey = "data_collection_consent_granted_utc_ticks";
    public const string DeclinedAtKey = "data_collection_consent_declined_utc_ticks";
    public const string RevokedAtKey = "data_collection_consent_revoked_utc_ticks";

    public static readonly TimeSpan DeclineWait = TimeSpan.FromHours(24);

    private readonly IConsentStorage _storage;
    private readonly Func<DateTimeOffset> _clock;

    public ConsentPolicy(IConsentStorage storage, Func<DateTimeOffset>? clock = null)
    {
        _storage = storage;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsGranted => _storage.GetBool(GrantedKey, false);

    public DateTimeOffset? GrantedAt => Read(GrantedAtKey);
    public DateTimeOffset? DeclinedAt => Read(DeclinedAtKey);
    public DateTimeOffset? RevokedAt => Read(RevokedAtKey);

    /// <summary>When a declined question may be asked again, or null when nothing was declined.</summary>
    public DateTimeOffset? NextPromptAt => DeclinedAt + DeclineWait;

    public ConsentState State
    {
        get
        {
            if (IsGranted)
                return ConsentState.Granted;

            var revoked = RevokedAt;
            var declined = DeclinedAt;

            // The most recent explicit choice wins.
            if (revoked is not null && (declined is null || revoked >= declined))
                return ConsentState.Revoked;

            if (declined is not null)
            {
                var elapsed = _clock() - declined.Value;
                // A negative interval means the device clock was moved back: do not wait for it to catch up.
                if (elapsed >= TimeSpan.Zero && elapsed < DeclineWait)
                    return ConsentState.DeclinedWaiting;
            }

            return ConsentState.NoDecision;
        }
    }

    /// <summary>True when the first-run question should be put to the user now.</summary>
    public bool ShouldPrompt => State == ConsentState.NoDecision;

    /// <summary>Records consent. Idempotent: an existing grant keeps its original timestamp.</summary>
    public void Grant()
    {
        if (IsGranted)
            return;

        _storage.SetBool(GrantedKey, true);
        _storage.SetLong(GrantedAtKey, _clock().UtcTicks);
    }

    /// <summary>Records "Not now". Never enables anything; ignored while consent is granted.</summary>
    public void Decline()
    {
        if (IsGranted)
            return;

        _storage.SetLong(DeclinedAtKey, _clock().UtcTicks);
    }

    /// <summary>Withdraws a granted consent. Without a grant there is nothing to withdraw, so the state is unchanged.</summary>
    public void Revoke()
    {
        if (!IsGranted)
            return;

        _storage.SetBool(GrantedKey, false);
        _storage.SetLong(RevokedAtKey, _clock().UtcTicks);
    }

    private DateTimeOffset? Read(string key)
    {
        var ticks = _storage.GetLong(key, 0);
        if (ticks <= 0 || ticks > DateTimeOffset.MaxValue.UtcTicks)
            return null;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}

/// <summary>What the coordinator did on one launch/resume check.</summary>
public enum ConsentPromptOutcome
{
    NotNeeded,
    Busy,
    NotReady,
    Granted,
    Declined
}

public enum ConsentChoice
{
    Agree,
    NotNow
}

/// <summary>
/// Decides when the consent question is shown and records the answer. UI, "can we show a modal right now" and
/// "turn collection on" are injected, so the rules (once until answered, 24 hours after a decline, never twice at
/// the same time) are testable without a device.
/// </summary>
public sealed class ConsentPromptCoordinator
{
    private readonly ConsentPolicy _policy;
    private readonly Func<bool> _canShowNow;
    private readonly Func<Task<ConsentChoice?>> _show;
    private readonly Func<Task> _onGranted;
    private int _busy;

    public ConsentPromptCoordinator(ConsentPolicy policy, Func<bool> canShowNow, Func<Task<ConsentChoice?>> show, Func<Task> onGranted)
    {
        _policy = policy;
        _canShowNow = canShowNow;
        _show = show;
        _onGranted = onGranted;
    }

    /// <summary>Call at launch and whenever the app returns to the foreground. Safe to call repeatedly and concurrently.</summary>
    public async Task<ConsentPromptOutcome> CheckAsync()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return ConsentPromptOutcome.Busy;

        try
        {
            if (!_policy.ShouldPrompt)
                return ConsentPromptOutcome.NotNeeded;

            if (!_canShowNow())
                return ConsentPromptOutcome.NotReady;

            var choice = await _show().ConfigureAwait(false);
            if (choice is null)
                return ConsentPromptOutcome.NotReady;   // the question could not be shown: that is not an answer

            if (choice == ConsentChoice.Agree)
            {
                _policy.Grant();
                try
                {
                    await _onGranted().ConfigureAwait(false);
                }
                catch
                {
                    // Turning collection on must never break the app; the grant itself is already stored.
                }

                return ConsentPromptOutcome.Granted;
            }

            _policy.Decline();
            return ConsentPromptOutcome.Declined;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }
}
