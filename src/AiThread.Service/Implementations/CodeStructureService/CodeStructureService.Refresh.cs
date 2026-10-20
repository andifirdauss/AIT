using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    // A queue of one per project: a new request cancels the refresh before it (waiting, or already scanning) and
    // takes its place, so threads finishing in several CLIs at once end in one scan of the latest files. The short
    // delay lets a burst of requests collapse before any scanning starts.
    public void Refresh(Guid projectId)
    {
        var entry = _projects.GetOrAdd(projectId, _ => new Entry());
        var refresh = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        lock (entry)
        {
            entry.Refresh?.Cancel();
            entry.Refresh = refresh;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(RefreshDelay, refresh.Token);
                await SnapshotAsync(projectId, rescan: true, refresh.Token);
            }
            catch (OperationCanceledException)
            {
                // Replaced by a newer request, or shutting down.
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Background refresh of the code structure of project {ProjectId} failed", projectId);
            }
            finally
            {
                // Cancel only ever runs under this lock, so the source is no longer reachable once it is removed here.
                lock (entry)
                {
                    if (entry.Refresh == refresh) entry.Refresh = null;
                }
                refresh.Dispose();
            }
        }, CancellationToken.None);
    }
}
