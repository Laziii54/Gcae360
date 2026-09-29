namespace CarScanner.ViewModels;

public partial class ProViewModel
{
// No test/development texts are shown to users (also not in Debug builds).
    public bool ShowTestPromoHint => false;

    public string TestPromoHint => string.Empty;
}