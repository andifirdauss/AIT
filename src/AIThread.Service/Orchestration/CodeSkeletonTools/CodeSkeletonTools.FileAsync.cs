using System.Text;
using AIThread.Service.Models;
using AIThread.Service.Workspace;
using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    private async Task<string> FileAsync(ProjectContext project, WorkspaceScope scope, string full, string? name, CancellationToken cancellationToken)
    {
        var display = scope.ToDisplay(full);
        if (Read(full) is not { } file)
            return $"Error: no outline for '{display}' (C#, PHP, JavaScript/TypeScript, Python, Java and Kotlin files up to 2 MB only). Use read_file.";

        var types = name is null ? file.Outline.Types : file.Outline.Types.Where(t => TypeMatches(t, name)).ToList();
        if (name is not null && types.Count == 0) return $"No type '{name}' in {display}.";
        var functions = name is null ? file.Outline.Functions : [];

        var calls = scope.IsInside(full)
            ? await CallNotesAsync(project.ProjectId, scope.ToRelative(full), file, types, functions, cancellationToken)
            : new Dictionary<OutlineMember, string>();
        var builder = new StringBuilder();
        builder.Append($"{display} ({file.Language}, {file.Lines.Length} lines)\n");
        string? currentNamespace = null;
        foreach (var type in types)
        {
            if (type.Namespace is { } ns && ns != currentNamespace) builder.Append($"namespace {ns}\n");
            currentNamespace = type.Namespace;
            builder.Append($"{type.Kind} {(type.Container is null ? type.Name : $"{type.Container}.{type.Name}")}{(type.IsPartial ? " (partial)" : string.Empty)} {Range(type.Line, type.EndLine)}\n");
            foreach (var member in type.Members) AppendMember(builder, member, calls);
        }
        if (functions.Count > 0)
        {
            builder.Append("functions\n");
            foreach (var function in functions) AppendMember(builder, function, calls);
        }
        if (types.Count == 0 && functions.Count == 0) builder.Append("No declarations found; use read_file.\n");
        return builder.ToString();
    }
}
