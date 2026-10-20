using System.Text;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    // "UserService" or "Outer.Inner": every file that declares it (a partial class has several), outlined from disk.
    private async Task<string> ClassAsync(ProjectContext project, WorkspaceScope scope, string name, CancellationToken cancellationToken)
    {
        var snapshot = await structure.GetSnapshotAsync(project.ProjectId, cancellationToken);
        var dot = name.LastIndexOf('.');
        var typeName = dot < 0 ? name : name[(dot + 1)..];
        var matches = snapshot.Symbols.Types(typeName).Where(m => TypeMatches(m.Type, name)).ToList();
        if (matches.Any(m => m.Type.Name == typeName)) matches = matches.Where(m => m.Type.Name == typeName).ToList();
        if (matches.Count == 0) return $"No type named '{name}' in the project outline. Try find_symbol, or give path.";

        var paths = matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count > MaxClassFiles)
        {
            var list = new StringBuilder($"{paths.Count} files declare a type named '{name}'; call code_outline with path and name for one of them:\n");
            foreach (var (path, type) in matches.Take(MaxClassMatches))
                list.Append($"- {path}:{type.Line} {type.Kind} {(type.Container is null ? type.Name : $"{type.Container}.{type.Name}")}{(type.Namespace is null ? string.Empty : $" ({type.Namespace})")}\n");
            return list.ToString();
        }

        var builder = new StringBuilder();
        foreach (var path in paths)
            builder.Append(await FileAsync(project, scope, Path.Combine(scope.Root, path), name, cancellationToken)).Append('\n');
        return Limit(builder.ToString().TrimEnd() + "\n");
    }
}
