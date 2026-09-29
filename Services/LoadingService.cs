using System.Threading;
namespace CarScanner.Services;
public sealed class LoadingService
{
    private int _active;
    public bool IsLoading => Volatile.Read(ref _active)>0;
    public event EventHandler<bool>? Changed;
    public async Task RunAsync(Func<Task> action){Interlocked.Increment(ref _active);Changed?.Invoke(this,true);try{await action();}finally{Interlocked.Decrement(ref _active);Changed?.Invoke(this,IsLoading);}}
}
