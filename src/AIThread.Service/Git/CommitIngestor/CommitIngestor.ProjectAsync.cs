using AIThread.Service.Models;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // The project with its key; null when it is not registered or has no key.
    private async Task<ProjectContext?> ProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await projectService.GetContextAsync(projectId, cancellationToken) is { Key: { Length: > 0 } } project ? project : null;
}
