namespace CarScanner.Api.Services.Email;

/// <summary>
/// The one rule for "this action needs a verified email". Used only by actions where owning the address matters
/// (starting a web payment, buying a VIN report). It is never used for scanning, live data, reading or clearing
/// codes, sign-in, account deletion, existing subscriptions or store purchase handling.
///
/// Existing accounts are unverified until they really verify; nothing here marks anyone verified.
///
/// The rule applies only while verification can actually be completed: when the server has no email settings,
/// nobody could ever verify, so enforcing it would lock every account out of paying. It can also be switched off
/// explicitly with GCS_REQUIRE_VERIFIED_EMAIL=false (or Email:RequireVerified=false).
/// </summary>
public static class EmailVerificationGate
{
    /// <summary>The error code the app and the web pages recognise (same style as INVALID_LOGIN, TOO_MANY…).</summary>
    public const string ErrorCode = "EMAIL_NOT_VERIFIED";

    public const int ErrorStatus = StatusCodes.Status403Forbidden;

    public const string ErrorMessage = "Verify your email address to continue.";

    public static bool IsEnforced(EmailVerificationService verification, IConfiguration config)
    {
        var setting = config["Email:RequireVerified"] ?? Environment.GetEnvironmentVariable("GCS_REQUIRE_VERIFIED_EMAIL");
        return verification.Enabled && !string.Equals(setting?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the action may go ahead: verified, or the rule is not in force.</summary>
    public static async Task<bool> IsSatisfiedAsync(EmailVerificationService verification, IConfiguration config, string accountId, CancellationToken ct) =>
        !IsEnforced(verification, config) || await verification.Store.IsVerifiedAsync(accountId, ct);

    /// <summary>The standard refusal: 403 with { success:false, code:"EMAIL_NOT_VERIFIED", message, canVerify:true }.</summary>
    public static IResult Refusal() =>
        Results.Json(new { success = false, code = ErrorCode, message = ErrorMessage, canVerify = true }, statusCode: ErrorStatus);
}
