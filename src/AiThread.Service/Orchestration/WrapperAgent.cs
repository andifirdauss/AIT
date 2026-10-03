using System.Text;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

// Pools every executor result and turns it into one reply for the developer.
public sealed class WrapperAgent(INineRouterService router, NodeConcurrencyLimiter limiter, NodePromptBuilder prompts)
{
    private const int MaxDiffLinesPerTask = 60;

    // plan and execution include the verification fix tasks; verification is VerificationResult.ForModels.
    public async Task<(string Text, int Tokens)> WrapAsync(
        AiNode node,
        string userRequest,
        ThreadPlan plan,
        ExecutionSummary execution,
        string? verification,
        Func<string, Task> onDelta,
        CancellationToken cancellationToken)
    {
        var systemPrompt = await prompts.BuildAsync(AgentPrompts.Wrapper, node, cancellationToken);
        using var lease = await limiter.AcquireAsync(node, cancellationToken);
        var result = await router.ChatAsync(new ChatCompletionRequest
        {
            Model = node.Model,
            Messages =
            [
                ChatMessages.System(systemPrompt),
                ChatMessages.User(BuildReport(userRequest, plan, execution, verification))
            ],
            Temperature = node.Temperature ?? 0.3,
            MaxTokens = node.MaxTokens
        }, onDelta, cancellationToken);

        return (result.Content, result.TotalTokens);
    }

    // Used when no wrapper is configured or the wrapper call fails, so the developer still gets the facts.
    public static string FallbackSummary(ThreadPlan plan, ExecutionSummary execution, VerificationReport? verification = null)
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

        if (verification?.Rounds.LastOrDefault() is { } last)
        {
            var outcome = verification.Outcome switch
            {
                VerificationReport.Passed => "✅ lolos",
                VerificationReport.Failed => "⚠️ masih ada masalah",
                _ => "❔ tidak dapat dipastikan"
            };
            builder.Append($"\n**Verifikasi:** {outcome}");
            if (last.Build is { } build) builder.Append($" — {build.Headline}");
            if (last.Review is { Error: null } review) builder.Append(review.Approved ? " · reviewer setuju" : $" · reviewer: {review.Issues.Count} masalah");
            builder.Append('\n');
            foreach (var error in last.Build?.Errors.Take(10) ?? []) builder.Append($"- `{error}`\n");
            foreach (var issue in last.Review?.Issues ?? []) builder.Append($"- `{issue.File}` — {issue.Problem}\n");
        }
        return builder.ToString();
    }

    private static string BuildReport(string userRequest, ThreadPlan plan, ExecutionSummary execution, string? verification)
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
            var task = plan.Tasks.LastOrDefault(t => t.Id == result.TaskKey);
            builder.Append($"\n## {result.TaskKey} · {task?.Title ?? result.File} — {result.Status}\n");
            builder.Append($"File: {result.File} ({task?.Action ?? "edit"})\n");
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

        if (!string.IsNullOrWhiteSpace(verification))
            builder.Append("\n# Verification (build and review after the tasks above)\n").Append(verification.Trim()).Append('\n');

        return builder.ToString();
    }
}
