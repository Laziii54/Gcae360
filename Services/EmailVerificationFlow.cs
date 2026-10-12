using CarScanner.Services.Localization;

namespace CarScanner.Services;

/// <summary>Why the verification dialog is being opened.</summary>
public enum EmailVerificationEntry
{
    /// <summary>The server just sent a code (registration): go straight to code entry.</summary>
    CodeAlreadySent,

    /// <summary>The user chose "Verify email" in the account page: send a code first.</summary>
    SendFirst,

    /// <summary>The server refused a restricted action (EMAIL_NOT_VERIFIED): explain, then send a code.</summary>
    Restricted,

    /// <summary>The server could not send the email at registration: explain and offer to try again.</summary>
    FirstSendFailed
}

/// <summary>
/// The one place that walks a user through verifying their email: explain, send a code, ask for it, show what the
/// server said, offer a new code. It reports success only when the server confirms the code. Texts come from the
/// app's language system (Georgian written out, other languages through the translation packs).
/// </summary>
public static class EmailVerificationFlow
{
    private const int MaxRounds = 6;

    private static string LT(string ka, string en) => LanguageCatalog.UsesEnglishBase ? LocalizationService.T(en) : ka;

    private static Page? CurrentPage => Application.Current?.Windows.FirstOrDefault()?.Page;

    /// <summary>Returns true only when the server confirmed that the email is verified.</summary>
    public static async Task<bool> RunAsync(AccountService account, EmailVerificationEntry entry, CancellationToken token = default)
    {
        var page = CurrentPage;
        if (page is null || !account.IsSignedIn)
            return false;

        var title = LT("დაადასტურე ელფოსტა", "Verify your email");
        var later = LT("მოგვიანებით", "Later");

        if (entry == EmailVerificationEntry.Restricted)
        {
            var go = await page.DisplayAlertLocalizedAsync(
                LT("ამისთვის ელფოსტა უნდა დადასტურდეს", "Verify your email to continue"),
                LT("ამ მოქმედებისთვის დადასტურებული ელფოსტაა საჭირო. კოდს გამოვგზავნით შენი ანგარიშის მისამართზე.",
                   "This needs a verified email address. We can send a code to the address on your account."),
                LT("კოდის გაგზავნა", "Send code"),
                later);
            if (!go)
                return false;
            entry = EmailVerificationEntry.SendFirst;
        }

        var needSend = entry != EmailVerificationEntry.CodeAlreadySent;
        var failureNote = entry == EmailVerificationEntry.FirstSendFailed;

        for (var round = 0; round < MaxRounds; round++)
        {
            if (failureNote)
            {
                // The email could not be sent: say so, and let the user retry (never pretend a code is on its way).
                var again = await page.DisplayAlertLocalizedAsync(
                    title,
                    LT("დამადასტურებელი წერილი ვერ გაიგზავნა. სცადე ცოტა ხანში.", "We couldn't send the verification email. Please try again in a moment."),
                    LT("ხელახლა გაგზავნა", "Send again"),
                    later);
                if (!again)
                    return false;
                failureNote = false;
                needSend = true;
            }

            if (needSend)
            {
                var sent = await account.SendEmailCodeAsync(token);
                needSend = false;
                if (!sent.Success)
                {
                    var retry = await page.DisplayAlertLocalizedAsync(
                        title,
                        LocalizationService.T(sent.Message),
                        LT("ხელახლა გაგზავნა", "Send again"),
                        later);
                    if (!retry)
                        return false;
                    needSend = true;
                    continue;
                }

                if (sent.Code == "already_verified")
                    return true;
            }

            var code = await page.DisplayPromptLocalizedAsync(
                title,
                LT("ელფოსტაზე გამოგიგზავნეთ 6-ნიშნა კოდი. შეიყვანე აქ მისამართის დასადასტურებლად. წერილში ღილაკიც არის, თუ ბმული გირჩევნია.",
                   "We sent a 6-digit code to your email. Enter it here to verify your address. The email also has a button if you prefer a link."),
                LT("დადასტურება", "Verify"),
                later,
                "123456",
                maxLength: 6,
                keyboard: Keyboard.Numeric);

            if (code is null)
                return false;                                            // "Later"

            var result = await account.VerifyEmailCodeAsync(code, token);
            if (result.Success)
                return true;

            var tryAgain = await page.DisplayAlertLocalizedAsync(
                LT("ელფოსტა არ დადასტურდა", "Email not verified"),
                LocalizationService.T(result.Message),
                LT("თავიდან ცდა", "Try again"),
                LT("ახალი კოდის გაგზავნა", "Send new code"));
            if (!tryAgain)
                needSend = true;                                         // expired, burned or lost: a new code
        }

        return false;
    }
}
