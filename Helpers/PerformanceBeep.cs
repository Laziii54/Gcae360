namespace CarScanner.Helpers;

/// <summary>Short beep + haptic tap when a 0–100 run reaches the target speed.</summary>
public static class PerformanceBeep
{
    public static void Play()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
#if ANDROID
                var tone = new Android.Media.ToneGenerator(Android.Media.Stream.Music, 100);
                tone.StartTone(Android.Media.Tone.PropBeep2, 350);
                _ = Task.Delay(600).ContinueWith(_ => { try { tone.Release(); } catch { } });
#elif IOS || MACCATALYST
                // 1057 = short system "tink" sound.
                new AudioToolbox.SystemSound(1057).PlaySystemSound();
#endif
            }
            catch
            {
                // Sound is optional.
            }

            try
            {
                HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
            }
            catch
            {
                // Haptics are optional.
            }
        });
    }
}
