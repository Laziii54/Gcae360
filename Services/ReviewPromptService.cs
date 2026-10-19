#if IOS
using StoreKit;
using UIKit;
#elif ANDROID
using Xamarin.Google.Android.Play.Core.Review;
#endif

namespace CarScanner.Services;

/// <summary>
/// Asks the store's own native rating dialog to show (Google Play In-App
/// Review / SKStoreReviewController). The app only ever requests it; the
/// store decides whether anything is actually shown, and both platforms
/// enforce their own quota (Apple: at most a few times a year), so calling
/// this too often has no effect beyond the first allowed prompt.
/// </summary>
public sealed class ReviewPromptService
{
    private const string LastPromptKey = "review.lastPromptAt";
    private static readonly TimeSpan MinInterval = TimeSpan.FromDays(90);

    /// <summary>
    /// Call at a positive moment (a clean health scan, a cleared fault code),
    /// never right after an error or on first launch. Safe to call often —
    /// it throttles itself and never throws into the caller.
    /// </summary>
    public async Task MaybeRequestAsync()
    {
        try
        {
            var lastTicks = Preferences.Default.Get(LastPromptKey, 0L);
            if (lastTicks > 0)
            {
                var last = new DateTime(lastTicks, DateTimeKind.Utc);
                if (DateTime.UtcNow - last < MinInterval)
                    return;
            }

            Preferences.Default.Set(LastPromptKey, DateTime.UtcNow.Ticks);
            await RequestPlatformReviewAsync();
        }
        catch
        {
            // A rating prompt must never crash or block the feature it follows.
        }
    }

#if IOS
    private static Task RequestPlatformReviewAsync()
    {
        var scene = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .FirstOrDefault();

        if (scene is not null)
            SKStoreReviewController.RequestReview(scene);

        return Task.CompletedTask;
    }
#elif ANDROID
    private static async Task RequestPlatformReviewAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
            return;

        var manager = ReviewManagerFactory.Create(activity);

        var infoListener = new GmsTaskListener();
        manager.RequestReviewFlow().AddOnCompleteListener(infoListener);
        var infoResult = await infoListener.Task.WaitAsync(TimeSpan.FromSeconds(8));
        if (infoResult is not ReviewInfo reviewInfo)
            return;

        var launchListener = new GmsTaskListener();
        manager.LaunchReviewFlow(activity, reviewInfo).AddOnCompleteListener(launchListener);
        await launchListener.Task.WaitAsync(TimeSpan.FromSeconds(8));
    }

    private sealed class GmsTaskListener : Java.Lang.Object, Android.Gms.Tasks.IOnCompleteListener
    {
        private readonly TaskCompletionSource<Java.Lang.Object?> _done =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public System.Threading.Tasks.Task<Java.Lang.Object?> Task => _done.Task;

        public void OnComplete(Android.Gms.Tasks.Task task)
        {
            if (task.IsSuccessful)
                _done.TrySetResult(task.Result);
            else
                _done.TrySetResult(null);
        }
    }
#else
    private static Task RequestPlatformReviewAsync() => Task.CompletedTask;
#endif
}
