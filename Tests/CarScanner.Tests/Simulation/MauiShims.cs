// Test-side stand-ins for the two MAUI APIs that BluetoothService.cs uses outside its #if ANDROID blocks, so the REAL BLE
// write/notify/resync code can be compiled into this project and exercised with a fake GATT characteristic. They exist only in
// the test assembly. If BluetoothService.cs starts using another MAUI API outside #if ANDROID / #if IOS, add it here.

/// <summary>Runs the action at once; the real MainThread marshals to the UI thread, which tests do not have.</summary>
public static class MainThread
{
    public static void BeginInvokeOnMainThread(Action action) => action();
}

namespace Microsoft.Maui.Storage
{
    public interface IPreferences
    {
        T Get<T>(string key, T defaultValue);
    }

    /// <summary>Preferences with nothing stored: every Get returns the default the caller passes (as a fresh install does).</summary>
    public static class Preferences
    {
        public static IPreferences Default { get; } = new EmptyPreferences();

        private sealed class EmptyPreferences : IPreferences
        {
            public T Get<T>(string key, T defaultValue) => defaultValue;
        }
    }
}
