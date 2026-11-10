using System.Text;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // The small files the request itself names, whole, the way read_file shows them: reading them would cost the planner
    // a turn of its own. Files is what the section holds, so the outline can leave them out.
    private static async Task<(string? Section, HashSet<string> Files)> SmallFilesAsync(PlannerInput input, WorkspaceTools tools, CancellationToken cancellationToken)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (input.LabelsFromSession is not null) return (null, files);

        var builder = new StringBuilder();
        foreach (var file in (input.Files ?? []).Take(MaxInlinedFiles))
        {
            var text = await tools.ReadFileAsync(file, null, null, cancellationToken);
            var tooLong = text.Count(c => c == '\n') > MaxInlinedFileLines + 1 || builder.Length + text.Length > MaxInlinedChars;
            if (tooLong || text.StartsWith("Error:", StringComparison.Ordinal)) continue;
            builder.Append(text).Append('\n');
            files.Add(file);
        }
        return (builder.Length == 0 ? null : builder.ToString(), files);
    }
}
