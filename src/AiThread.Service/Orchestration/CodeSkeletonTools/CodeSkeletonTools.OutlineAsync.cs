using AiThread.Service.Models;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    public async Task<string> OutlineAsync(ProjectContext project, WorkspaceScope scope, string? path, string? name, CancellationToken cancellationToken)
    {
        if (path is null && name is null) return "Error: give path (a file or folder) or name (a class).";
        try
        {
            if (path is null) return await ClassAsync(project, scope, name!, cancellationToken);
            if (!scope.TryResolveForRead(path, out var full, out var error)) return $"Error: {error}";
            if (Directory.Exists(full)) return await FolderAsync(project, scope, full, cancellationToken);
            if (!File.Exists(full)) return $"Error: '{path}' does not exist.";
            return Limit(await FileAsync(project, scope, full, name, cancellationToken));
        }
        catch (AppValidationException)
        {
            return "Error: the project could not be scanned (its folder is missing on this machine).";
        }
    }
}
