using System.Text.RegularExpressions;

namespace CarScanner.Services;

/// <summary>
/// What counts as an email address when an account is created. One source for both sides: the app checks before
/// it sends, the backend checks again (the backend never trusts the app). Used for registration only, so accounts
/// that already exist keep signing in whatever their address looks like.
///
/// Accepted: one "@", a local part of up to 64 characters (letters, digits and . _ % + - ), no leading, trailing
/// or doubled dots, and a domain of dot-separated labels (letters, digits, hyphen; no hyphen at either end) with
/// at least one dot and a final label of 2 or more letters. Whitespace, display names ("Name &lt;a@b.com&gt;"),
/// comments and quoted local parts are rejected.
/// </summary>
public static class EmailAddressRules
{
    public const int MaxLength = 254;

    private static readonly Regex Pattern = new(
        @"^(?<local>[A-Za-z0-9](?:[A-Za-z0-9._%+\-]*[A-Za-z0-9])?)@(?<domain>(?:[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,24})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValidForRegistration(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var value = email.Trim();
        if (value.Length is < 6 or > MaxLength)
            return false;

        var match = Pattern.Match(value);
        return match.Success &&
               match.Groups["local"].Length <= 64 &&
               !value.Contains("..", StringComparison.Ordinal);
    }
}
