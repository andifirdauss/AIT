using System.Text;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed partial class ExecutorAgent
{
    private static async Task<string> BuildReferencesAsync(ExecutorJob job, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (var reference in job.Task.Context ?? [])
        {
            if (builder.Length >= MaxContextChars) break;
            // Read-only excerpts may come from outside the project root (e.g. a sibling project used as a model).
            if (!job.Scope.TryResolveForRead(reference.File, out var fullPath, out _) || !File.Exists(fullPath)) continue;

            var state = await TextFileIo.ReadAsync(fullPath, cancellationToken);
            var lines = WorkspaceTools.SplitLines(state.Content);
            if (lines.Length == 0) continue;

            var start = Math.Clamp(reference.StartLine ?? 1, 1, lines.Length);
            var end = Math.Clamp(reference.EndLine ?? lines.Length, start, lines.Length);
            if (end - start + 1 > MaxContextLinesPerRef) end = start + MaxContextLinesPerRef - 1;

            builder.Append($"### {job.Scope.ToDisplay(fullPath)} (lines {start}-{end})");
            if (!string.IsNullOrWhiteSpace(reference.Note)) builder.Append($" — {reference.Note}");
            builder.Append($"\n```{LanguageOf(fullPath)}\n");
            for (var i = start; i <= end; i++) builder.Append(lines[i - 1]).Append('\n');
            builder.Append("```\n");
        }

        return builder.Length <= MaxContextChars ? builder.ToString() : builder.ToString()[..MaxContextChars] + "\n... (references truncated)\n";
    }
}
