using AIThread.Service.Databases;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The project's databases for the database tools; a failure only costs those tools, not the thread.
    private async Task<IReadOnlyList<DatabaseTarget>> DatabasesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        try
        {
            return await projectDatabases.GetTargetsAsync(projectId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not load the databases of project {ProjectId}.", projectId);
            return [];
        }
    }
}
