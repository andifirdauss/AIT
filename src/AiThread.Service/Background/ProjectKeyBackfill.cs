using AiThread.Service.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Background;

// Once per start: recomputes every project's key (a remote or .aithread/project.json may have been added since) and
// gives memory written before keys existed the key of its project, so it is shared with the project's other clones.
// Runs after MemoryStorageInitializer, so the configured memory stores are already in use.
public sealed class ProjectKeyBackfill(IProjectService projectService, ILogger<ProjectKeyBackfill> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting before touching the disk and the memory stores.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        try
        {
            await projectService.RefreshKeysAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Project keys could not be refreshed.");
        }
    }
}
