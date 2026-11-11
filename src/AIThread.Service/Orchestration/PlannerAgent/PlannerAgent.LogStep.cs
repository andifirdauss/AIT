using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // One activity line per planner turn (Log & diagnostics, the thread page, the server log): the tools it called and
    // where the time went, so a long planning phase shows its progress. toolTime: the turn's tools, zero without tools.
    private static void LogStep(ThreadRecorder recorder, PlannerRun run, int step, NodeReply reply, IReadOnlyList<ToolCall> calls, TimeSpan toolTime)
    {
        var tools = calls.Count == 0
            ? Lang.T("jawaban teks", "text answer")
            : string.Join(", ", calls.GroupBy(c => c.Name).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key));
        var queued = reply.Queued >= TimeSpan.FromSeconds(1) ? Lang.T($", antre {FinalReport.Duration(reply.Queued)}", $", queued {FinalReport.Duration(reply.Queued)}") : string.Empty;
        var toolPart = calls.Count == 0 ? string.Empty : $" · tool {FinalReport.Duration(toolTime)}";
        var tokens = FinalReport.Tokens(reply.Result.TotalTokens);
        recorder.Log(ActivityLevel.Info, Lang.T(
            $"Planner langkah {step}/{run.MaxSteps} (`{reply.Node.Model}`): {tools} · model {FinalReport.Duration(reply.Elapsed)}{queued}{toolPart} · {tokens} token",
            $"Planner step {step}/{run.MaxSteps} (`{reply.Node.Model}`): {tools} · model {FinalReport.Duration(reply.Elapsed)}{queued}{toolPart} · {tokens} tokens"));
    }
}
