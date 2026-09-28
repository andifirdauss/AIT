using System.Text;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

// Pools every executor result and turns it into one reply for the developer.
public sealed class WrapperAgent(INineRouterService router)
{
    private const int MaxDiffLinesPerTask = 60;

    public async Task<(string Text, int Tokens)> WrapAsync(
        AiNode node,
        string userRequest,
        ThreadPlan plan,
        ExecutionSummary execution,
        Func<string, Task> onDelta,
        CancellationToken cancellationToken)
    {
        var result = await router.ChatAsync(new ChatCompletionRequest
        {
            Model = node.Model,
            Messages =
            [
                ChatMessages.System(AgentPrompts.WithNodeInstructions(AgentPrompts.Wrapper, node.SystemPrompt)),
                ChatMessages.User(BuildReport(userRequest, plan, execution))
            ],
            Temperature = node.Temperature ?? 0.3,
            MaxTokens = node.MaxTokens
        }, onDelta, cancellationToken);

        return (result.Content, result.TotalTokens);
    }

    // Used when no wrapper is configured or the wrapper call fails, so the developer still gets the facts.
    public static string FallbackSummary(ThreadPlan plan, ExecutionSummary execution)
    {
        var builder = new StringBuilder();
        builder.Append($"**Ringkasan:** {plan.Summary}\n\n");
        foreach (var result in execution.Results)
        {
            var icon = result.Status switch
            {
                ThreadTaskStatus.Succeeded => "✅",
                ThreadTaskStatus.Skipped => "⏭️",
                _ => "❌"
            };
            builder.Append($"- {icon} **{result.TaskKey}** `{result.File}` — {(result.Status == ThreadTaskStatus.Succeeded ? result.Summary : result.Error)}\n");
        }
        return builder.ToString();
    }

    private static string BuildReport(string userRequest, ThreadPlan plan, ExecutionSummary execution)
    {
        var builder = new StringBuilder();
        builder.Append("# Developer request\n").Append(userRequest.Trim()).Append("\n\n");
        builder.Append("# Plan summary\n").Append(plan.Summary).Append("\n\n");

        if (plan.Contract.Count > 0)
        {
            builder.Append("# Contract\n");
            foreach (var item in plan.Contract) builder.Append($"- {item.Name}: {item.Value}\n");
            builder.Append('\n');
        }

        builder.Append($"# Results ({execution.Results.Count(r => r.Status == ThreadTaskStatus.Succeeded)}/{execution.Results.Count} succeeded)\n");
        foreach (var result in execution.Results)
        {
            var task = plan.Tasks.First(t => t.Id == result.TaskKey);
            builder.Append($"\n## {result.TaskKey} · {task.Title} — {result.Status}\n");
            builder.Append($"File: {result.File} ({task.Action})\n");
            if (result.Summary is not null) builder.Append($"Summary: {result.Summary}\n");
            if (result.Error is not null) builder.Append($"Error: {result.Error}\n");
            if (result.Diff is not null)
            {
                var lines = result.Diff.Split('\n');
                builder.Append("```diff\n").Append(string.Join('\n', lines.Take(MaxDiffLinesPerTask)));
                if (lines.Length > MaxDiffLinesPerTask) builder.Append($"\n... ({lines.Length - MaxDiffLinesPerTask} more lines)");
                builder.Append("\n```\n");
            }
        }

        return builder.ToString();
    }
}
