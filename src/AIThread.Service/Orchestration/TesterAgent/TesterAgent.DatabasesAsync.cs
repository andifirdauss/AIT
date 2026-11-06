using AIThread.Service.Databases;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // A failure only costs the database tools, not the test run.
    private async Task<IReadOnlyList<DatabaseTarget>> DatabasesAsync(ProjectContext project, CancellationToken cancellationToken)
    {
        try
        {
            return await projectDatabases.GetTargetsAsync(project.ProjectId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not load the databases of project {ProjectId} for testing.", project.ProjectId);
            return [];
        }
    }
}
