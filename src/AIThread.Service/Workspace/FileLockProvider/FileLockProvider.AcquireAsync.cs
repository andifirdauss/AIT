namespace AIThread.Service.Workspace;

public sealed partial class FileLockProvider
{
    public async Task<IDisposable> AcquireAsync(string fullPath, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(Path.GetFullPath(fullPath), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }
}
