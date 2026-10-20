namespace AIThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    public void Dispose()
    {
        // Leaves 9router running unless StopOnExit (handled by the worker); only the handle is released here.
        Interlocked.Exchange(ref _started, null)?.Dispose();
        _gate.Dispose();
    }
}
