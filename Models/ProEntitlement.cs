using SQLite;
namespace CarScanner.Models;

/// <summary>
/// The device's cached copy of what the server said about this installation.
///
/// Two independent sources are cached side by side because they must never
/// overwrite each other:
///  • the OBD360 account's entitlement (website purchase, account-linked store
///    purchase, admin/promo plan) — [IsPro], [ExpiresAt], [Source], [ProductId];
///  • an accountless App Store subscription that belongs to no account —
///    [AppleHasObd]…[AppleVerifiedAt].
///
/// Signing out clears only the first group, so an Apple subscription bought
/// without an account survives a sign-out (App Review 5.1.1(v)). Both groups
/// are only ever written from a server-verified answer; the server stays the
/// authority for anything that costs money.
/// </summary>
public sealed class ProEntitlement
{
    [PrimaryKey] public string Id { get; set; } = "local";

    // ---- OBD360 account entitlement ----
    public bool IsPro { get; set; }
    public bool VerifiedByServer { get; set; }
    public string Source { get; set; } = "none";
    public DateTime? ExpiresAt { get; set; }
    public string ProductId { get; set; } = string.Empty;

    // ---- Accountless App Store entitlement ----
    public bool AppleHasObd { get; set; }
    public bool AppleHasVin { get; set; }
    public DateTime? AppleObdExpiresAt { get; set; }
    public DateTime? AppleVinExpiresAt { get; set; }
    public string AppleProductId { get; set; } = string.Empty;
    public bool AppleVerifiedByServer { get; set; }
    public DateTime? AppleVerifiedAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
