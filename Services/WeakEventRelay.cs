namespace CarScanner.Services;

/// <summary>
/// Subscribes a short-lived object (a transient view model, created every time a page opens) to an event of a
/// long-lived service without keeping it alive. A plain <c>service.Event += handler</c> makes the service hold every
/// view model ever created: each language change then runs on all of them, and none can be collected. Callers must pass
/// <c>static</c> lambdas so nothing captures the owner.
/// </summary>
public static class WeakEventRelay
{
    public static void Attach<TOwner, TSource>(
        TOwner owner,
        TSource source,
        Action<TSource, EventHandler> subscribe,
        Action<TSource, EventHandler> unsubscribe,
        Action<TOwner> handler)
        where TOwner : class
        where TSource : class
    {
        var weak = new WeakReference<TOwner>(owner);
        EventHandler? relay = null;
        relay = (_, _) =>
        {
            if (weak.TryGetTarget(out var target))
                handler(target);
            else
                unsubscribe(source, relay!);
        };
        subscribe(source, relay);
    }
}
