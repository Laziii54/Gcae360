using Microsoft.Maui.ApplicationModel.Communication;

namespace CarScanner.Services;

public sealed class ContactService
{
    public const string ContactEmail = "zukalazashvili1999@gmail.com";
    public async Task SendAsync(string subject, string message)
    {
        try
        {
            if (Microsoft.Maui.ApplicationModel.Communication.Email.Default.IsComposeSupported)
            {
                await Microsoft.Maui.ApplicationModel.Communication.Email.Default.ComposeAsync(new EmailMessage { Subject = subject, Body = message, To = new List<string> { ContactEmail } });
                return;
            }
        }
        catch { }

        await Launcher.Default.OpenAsync($"mailto:{ContactEmail}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(message)}");
    }
}
