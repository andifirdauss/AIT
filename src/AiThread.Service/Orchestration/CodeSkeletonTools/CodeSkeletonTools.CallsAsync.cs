using System.Text;
using AiThread.Common.Enums;
using AiThread.Service.Models;
using AiThread.Service.Workspace;
using AiThread.Service.Workspace.Structure;

namespace AiThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    public async Task<string> CallsAsync(ProjectContext project, WorkspaceScope scope, string? path, string? name, CancellationToken cancellationToken)
    {
        if (path is null || name is null) return "Error: path and name are required.";
        if (!scope.TryResolve(path, out var full, out var error)) return $"Error: {error} Function calls are recorded for project files only.";
        if (!File.Exists(full)) return $"Error: '{path}' does not exist.";
        var relative = scope.ToRelative(full);
        if (Read(full) is not { } file) return $"Error: no outline for '{relative}' (C#, PHP, JavaScript/TypeScript, Python, Java and Kotlin files up to 2 MB only).";

        var spans = Matching(FunctionSpans.Of(file.Outline), name);
        if (spans.Count == 0) return $"No function or method '{name}' with a body in {relative}. code_outline lists its declarations.";

        var builder = new StringBuilder();
        foreach (var span in spans.Take(MaxOverloads))
        {
            builder.Append($"{span.Symbol}{(span.Signature is null ? string.Empty : $" — {span.Signature}")}\n{relative}:{span.Line}-{span.EndLine}\n");
            var key = FunctionSpans.Key(relative, span.Symbol, span.Signature);
            var scan = await store.GetAsync(project.ProjectId, key, cancellationToken);
            if (scan is not { Status: FunctionScanStatus.Done })
            {
                queue.Read(project.ProjectId, project.RootPath, relative, span.Line, span.EndLine);
                builder.Append("Calls: not recorded yet (queued for the tools node, which reads them in the background). Read the function if you need them now.\n");
            }
            else
            {
                var changed = scan.BodyHash != FunctionSpans.Hash(Body(file.Lines, span));
                if (changed) queue.Read(project.ProjectId, project.RootPath, relative, span.Line, span.EndLine);
                builder.Append($"Calls (recorded {scan.UpdatedAt ?? scan.CreatedAt:yyyy-MM-dd HH:mm} UTC{(changed ? "; the function changed since, so call lines may be off: read it before relying on them" : string.Empty)}):\n");
                if (scan.Calls.Count == 0) builder.Append("  (none into the project's own code)\n");
                foreach (var call in scan.Calls)
                {
                    builder.Append($"  {call.Line}: {call.Kind.ToString().ToLowerInvariant()} {call.Target}");
                    builder.Append(call.ResolvedPath is { } target
                        ? $" → {target}:{call.ResolvedLine}{(call.ResolvedSymbol is { } symbol ? " " + symbol : string.Empty)}\n"
                        : " → not found in the project\n");
                }
            }

            var callers = await store.CallersAsync(project.ProjectId, relative, CallTarget(span.Symbol), MaxCallers, cancellationToken);
            builder.Append(callers.Count == 0
                ? "Called by: none recorded (only functions read so far are known; find_references lists every use).\n"
                : "Called by (recorded so far; find_references lists every use):\n");
            foreach (var caller in callers) builder.Append($"  {caller.Path}:{caller.Line} {caller.Symbol}\n");
            builder.Append('\n');
        }
        if (spans.Count > MaxOverloads) builder.Append($"... {spans.Count - MaxOverloads} more overload(s).\n");
        return Limit(builder.ToString().TrimEnd() + "\n");
    }
}
