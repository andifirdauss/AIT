using System.Text;
using AIThread.Service.Models;
using AIThread.Service.Workspace;
using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    // The planner brief: the outline of each file the request names, until the budget is spent.
    public async Task<string?> BriefAsync(ProjectContext project, WorkspaceScope scope, IEnumerable<string> paths, int maxFiles, int maxChars, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (var path in paths.Take(maxFiles))
        {
            if (OutlineLanguages.Of(path) is null || !scope.TryResolve(path, out var full, out _) || !File.Exists(full)) continue;
            string outline;
            try
            {
                outline = await FileAsync(project, scope, full, null, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (outline.StartsWith("Error:", StringComparison.Ordinal)) continue;
            if (builder.Length + outline.Length > maxChars)
            {
                builder.Append($"- {scope.ToRelative(full)}: outline too long for the brief; call code_outline.\n");
                continue;
            }
            builder.Append(outline).Append('\n');
        }
        return builder.Length == 0 ? null : builder.ToString();
    }
}
