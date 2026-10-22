using System.Text;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class ExecutorAgent
{
    // Secrets in the file are shown as placeholders.
    private string FileSection(PlanTask task, TextFileState file, WorkspaceScope scope)
    {
        var language = LanguageOf(task.File);
        if (!file.Exists)
            return $"## Current content of {task.File}\n(the file does not exist yet — create it with one block whose SEARCH section is empty)\n";

        var content = secrets.RedactFile(task.File, file.Content, scope.Secrets.Keys);
        var lines = WorkspaceTools.SplitLines(content);
        var fullMax = options.Value.ExecutorFullFileMaxLines;
        if (lines.Length <= fullMax)
            return $"## Current content of {task.File}\n```{language}\n{content.TrimEnd('\n')}\n```\n";

        // Large file: show the planner's focus ranges with a margin, marking what is hidden.
        var ranges = (task.Focus ?? [])
            .Where(r => r.StartLine > 0 && r.EndLine >= r.StartLine)
            .Select(r => (Start: Math.Max(1, r.StartLine - ExcerptMargin), End: Math.Min(lines.Length, r.EndLine + ExcerptMargin)))
            .OrderBy(r => r.Start)
            .ToList();
        if (ranges.Count == 0) ranges.Add((1, fullMax));

        var merged = new List<(int Start, int End)>();
        foreach (var range in ranges)
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End + 1)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            else
                merged.Add(range);
        }

        var builder = new StringBuilder();
        builder.Append($"## Current content of {task.File} (excerpts — the file has {lines.Length} lines)\n");
        builder.Append("Only the excerpts below are shown. Copy SEARCH text from them; never include the \"lines ... not shown\" markers.\n");
        builder.Append($"```{language}\n");
        var cursor = 1;
        foreach (var (start, end) in merged)
        {
            if (start > cursor) builder.Append($"... (lines {cursor}-{start - 1} not shown) ...\n");
            for (var i = start; i <= end; i++) builder.Append(lines[i - 1]).Append('\n');
            cursor = end + 1;
        }
        if (cursor <= lines.Length) builder.Append($"... (lines {cursor}-{lines.Length} not shown) ...\n");
        builder.Append("```\n");
        return builder.ToString();
    }
}
