using System.Runtime.CompilerServices;
using CarScanner.Services;
using Xunit;

namespace CarScanner.Tests;

/// <summary>
/// Transient view models that subscribed to a long-lived service were never released (every page open added one more
/// listener). The relay holds the owner weakly. These tests use plain objects; they do not prove anything about a phone.
/// </summary>
public sealed class WeakEventRelayTests
{
    private sealed class Service
    {
        public event EventHandler? Changed;
        public int ListenerCount => Changed?.GetInvocationList().Length ?? 0;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Owner
    {
        public int Calls;
    }

    [Fact]
    public void ALiveOwner_ReceivesTheEvent()
    {
        var service = new Service();
        var owner = new Owner();
        WeakEventRelay.Attach(owner, service,
            static (source, handler) => source.Changed += handler,
            static (source, handler) => source.Changed -= handler,
            static target => target.Calls++);

        service.Raise();
        service.Raise();

        Assert.Equal(2, owner.Calls);
        Assert.Equal(1, service.ListenerCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AttachAndForget(Service service)
    {
        var owner = new Owner();
        WeakEventRelay.Attach(owner, service,
            static (source, handler) => source.Changed += handler,
            static (source, handler) => source.Changed -= handler,
            static target => target.Calls++);
        return new WeakReference(owner);
    }

    [Fact]
    public void ADroppedOwner_CanBeCollected_AndTheRelayRemovesItselfOnTheNextEvent()
    {
        var service = new Service();
        var weak = AttachAndForget(service);

        for (var i = 0; i < 3 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive);                 // the service no longer keeps the owner alive
        Assert.Equal(1, service.ListenerCount);

        service.Raise();                            // the dead owner's relay unsubscribes itself

        Assert.Equal(0, service.ListenerCount);
    }

    [Fact]
    public void ManyShortLivedOwners_DoNotAccumulateListeners()
    {
        var service = new Service();
        for (var i = 0; i < 50; i++)
            AttachAndForget(service);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        service.Raise();

        Assert.Equal(0, service.ListenerCount);
    }

    [Fact]
    public void TheTransientViewModels_UseTheRelay_NotAPlainSubscription()
    {
        var root = AppContext.BaseDirectory.Split("Tests")[0];
        foreach (var (file, plain) in new[]
        {
            ("AccelerationHistoryViewModel.cs", "_units.UnitSystemChanged +="),
            ("AccelerationLeaderboardViewModel.cs", "_localization.LanguageChanged +="),
            ("TechnicalInspectionViewModel.cs", "_localization.LanguageChanged +=")
        })
        {
            var code = File.ReadAllText(Path.Combine(root, "ViewModels", file));
            Assert.Contains("WeakEventRelay.Attach", code);
            Assert.DoesNotContain(plain, code);
        }
    }
}
