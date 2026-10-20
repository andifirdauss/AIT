namespace AIThread.Service.Interfaces;

public interface IFileLockProvider
{
    Task<IDisposable> AcquireAsync(string fullPath, CancellationToken cancellationToken);
}
