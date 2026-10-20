namespace AIThread.Service.Memory;

public sealed partial class ModuleRegistry
{
    // Returns early (true) when a module is created, so the duplicate check runs soon after. Several creations wake
    // the caller once.
    public async Task<bool> WaitForCreatedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var woken = await _signal.WaitAsync(timeout, cancellationToken);
        while (_signal.CurrentCount > 0) _signal.Wait(0, CancellationToken.None);
        return woken;
    }
}
