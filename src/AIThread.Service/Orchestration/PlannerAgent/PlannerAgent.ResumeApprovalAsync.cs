using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // Allowed: a waiting plan is accepted as it is (no model call), a waiting command runs and its output becomes the
    // paused call's result. "Always" also saves a grant per item. Denied (or answered with something else): the planner
    // hears so, with the developer's words, and goes on.
    public async Task<PlanningResult> ResumeApprovalAsync(
        PlannerInput input,
        PlannerConversation conversation,
        PlannerApproval approval,
        ApprovalDecision? decision,
        string reply,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var allowed = decision is ApprovalDecision.Once or ApprovalDecision.Always or ApprovalDecision.AlwaysPattern;
        // "Always allow the pattern" keeps the pattern of a command item ("npm install *"); the other items get a plain grant.
        if (decision is ApprovalDecision.Always or ApprovalDecision.AlwaysPattern && approval.Request.AllowAlways)
            foreach (var item in approval.Request.Items)
                await approvals.GrantAsync(input.Project.ProjectId, item.Kind,
                    decision == ApprovalDecision.AlwaysPattern && item.Pattern is { } pattern ? pattern : item.Target, recorder.Thread.Id, cancellationToken);

        var how = decision switch
        {
            ApprovalDecision.Always => Lang.T("selalu", "always"),
            ApprovalDecision.AlwaysPattern => Lang.T("selalu, pola", "always, pattern"),
            _ => Lang.T("sekali", "once")
        };
        recorder.Log(allowed ? ActivityLevel.Info : ActivityLevel.Warning, allowed
            ? Lang.T($"Developer mengizinkan ({how}): {Targets(approval)}", $"The developer allowed ({how}): {Targets(approval)}")
            : Lang.T($"Developer menolak: {Targets(approval)}", $"The developer denied: {Targets(approval)}"));

        if (allowed && approval.Plan is { } plan)
        {
            var node = input.Nodes.FirstOrDefault(n => n.Id == conversation.NodeId) ?? input.Nodes[0];
            return new PlanningResult(plan, conversation.Step, conversation.Tokens, conversation.CachedTokens, node, ToolsNodeTokens: conversation.ToolsNodeTokens);
        }

        var instruction = decision is null && !string.IsNullOrWhiteSpace(reply) ? $"\nThe developer replied instead (it may change the request):\n{reply.Trim()}" : string.Empty;
        string toolResult;
        if (allowed && approval.Command is { } command)
            toolResult = await runner.RunApprovedCommandAsync(command, input, recorder, progress, cancellationToken);
        else if (allowed && approval.Fetch is { } fetch)
            toolResult = await runner.RunApprovedFetchAsync(fetch, input, recorder, progress, cancellationToken);
        else if (approval.Fetch is not null)
            toolResult = $"Error: the developer did not allow fetching {Targets(approval)}. Do without it, or name the page in the summary for the developer to check.{instruction}";
        else if (approval.Plan is not null)
            toolResult = $"Plan rejected: the developer did not allow changing files outside the project root ({Targets(approval)}). Plan within the project root, or put the outside change in \"answer\" for the developer to make.{instruction}";
        else
            toolResult = $"Error: the developer did not allow this command. Do without it, or name in the summary what the developer should run themselves.{instruction}";

        return await ResumeAsync(input, conversation, toolResult, recorder, progress, cancellationToken);
    }
}
