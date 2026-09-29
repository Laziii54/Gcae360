namespace CarScanner.Services;

public static class AIConfig
{
    // Replace only this URL with the public HTTPS address of your CarScanner.Api deployment.
    // No AI key is stored in the MAUI app.
    public const string BackendBaseUrl = "https://georgian-car-scanner-api-cyawhhdygecng6bt.germanywestcentral-01.azurewebsites.net";
    public const string DiagnosePath = "/api/ai/diagnose";
    public const string ChatPath = "/api/ai/chat";
    public const string DefaultModel = "gemini-3.6-flash";
    public const int MaxRequestSeconds = 60;
}
