using System.Text;
using System.Text.Json.Nodes;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class ToolsNodeAgent
{
    // The line ranges report_findings named, read like read_file (same scope, secrets as placeholders, the read recorded)
    // and attached after the findings: exact text the cheap model never had to write out, which was its slowest turn.
    private async Task<string> ExcerptsAsync(IReadOnlyList<ToolsNodeExcerpt> excerpts, PlannerInput input, WorkspaceTools tools, HashSet<Guid> memoriesRead,
        string prefix, string actor, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        if (excerpts.Count == 0) return string.Empty;

        var builder = new StringBuilder("\n\nExact excerpts, copied by AIT from the files:\n");
        var ranges = 0;
        var lines = 0;
        var left = 0;
        foreach (var excerpt in excerpts.Distinct())
        {
            if (ranges == MaxExcerpts || lines >= MaxExcerptLines)
            {
                left++;
                continue;
            }
            var end = Math.Min(excerpt.EndLine, excerpt.StartLine + MaxExcerptLines - lines - 1);
            var args = new JsonObject { ["path"] = excerpt.Path, ["startLine"] = excerpt.StartLine, ["endLine"] = end };
            await PlannerToolRunner.AnnounceAsync(PlannerTools.ReadFile, args, actor, prefix, recorder, progress, cancellationToken);
            var outcome = await runner.RunAsync(PlannerTools.ReadFile, args, input, tools, memoriesRead, prefix, recorder, progress, cancellationToken);
            builder.Append('\n').Append(outcome.Output.TrimEnd()).Append('\n');
            ranges++;
            lines += end - excerpt.StartLine + 1;
        }
        if (left > 0) builder.Append($"\n({left} more excerpt(s) left out: at most {MaxExcerpts} ranges and {MaxExcerptLines} lines. Ask again for them.)\n");
        return builder.ToString();
    }
}
