using AiThread.Service.Models;
using AiThread.Service.Workspace;

namespace AiThread.Service.Interfaces;

public interface ICodeSkeletonTools
{
    Task<string> OutlineAsync(ProjectContext project, WorkspaceScope scope, string? path, string? name, CancellationToken cancellationToken);

    Task<string> CallsAsync(ProjectContext project, WorkspaceScope scope, string? path, string? name, CancellationToken cancellationToken);

    Task<string?> BriefAsync(ProjectContext project, WorkspaceScope scope, IEnumerable<string> paths, int maxFiles, int maxChars, CancellationToken cancellationToken);
}
