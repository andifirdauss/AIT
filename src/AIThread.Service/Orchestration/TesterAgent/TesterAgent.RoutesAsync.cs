using AIThread.Service.Models;
using AIThread.Service.Testing;
using AIThread.Service.Workspace;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // The endpoints the project's code declares, read after the change is applied so a new one is in. A scan that fails
    // only costs the route hints: the test runs on the plan's targets.
    private async Task<IReadOnlyList<CodeRoute>> RoutesAsync(ProjectContext project, CancellationToken cancellationToken)
    {
        try
        {
            var scope = new WorkspaceScope(project.RootPath, ProjectLocator.IgnoredDirectoriesFor(project.Stacks));
            return await Task.Run(() => AspNetRoutes.Scan(scope, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Routes of {Project} could not be read; the tester uses the plan's targets as they are.", project.Name);
            return [];
        }
    }
}
