namespace CarScanner.Helpers;

/// <summary>One light tap when a refresh the person started has finished. Never throws.</summary>
public static class RefreshHaptics
{
    public static void Completed()
    {
        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
        catch { /* no vibration hardware or permission: nothing to do */ }
    }
}
