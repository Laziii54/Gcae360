namespace CarScanner.Services;

/// <summary>
/// One place for "this is a PRO feature" checks. When the user has no PRO,
/// a short message is shown with a button that opens the PRO page.
/// </summary>
public static class ProGate
{
    public static async Task<bool> EnsureAsync(ProService pro, string featureKa, string featureEn)
    {
        if (await pro.EnsureProAsync())
            return true;

        var english = Preferences.Default.Get("language", "ქართული")
            .Equals("English", StringComparison.OrdinalIgnoreCase);

        var open = await Shell.Current.DisplayAlertAsync(
            "PRO",
            english
                ? $"{featureEn} is available with PRO. It unlocks right after activation."
                : $"{featureKa} ხელმისაწვდომია PRO-სთან ერთად. გააქტიურებისთანავე გაიხსნება.",
            english ? "Get PRO" : "PRO-ს გააქტიურება",
            english ? "Not now" : "არა ახლა");

        if (open)
            await Shell.Current.GoToAsync(nameof(Views.ProPage));
        return false;
    }
}
