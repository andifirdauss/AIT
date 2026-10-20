using Microsoft.Extensions.Logging;

namespace AIThread.Service.Business;

public sealed partial class BusinessDigestRunner
{
    public async Task RunAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var (key, name) in await collector.PendingProjectsAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await RunKeyAsync(key, name, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One project's failure (no model, router down) must not stop the others.
                    logger.LogWarning(ex, "Business memory update of {ProjectKey} failed.", key);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
