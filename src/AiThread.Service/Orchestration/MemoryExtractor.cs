using System.Text;
using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Orchestration;

// After a thread finishes, distills reusable project/knowledge memories into short memory (Redis).
public sealed class MemoryExtractor(
    INineRouterService router,
    IMemoryService memoryService,
    IActivityLogService activityLog,
    ILogger<MemoryExtractor> logger)
{
    private const int MaxMemories = 5;

    public async Task ExtractAsync(AiNode node, ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution, string finalText, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await memoryService.GetIndexAsync(thread.ProjectId, thread.Prompt, 40, cancellationToken);
            var result = await router.ChatAsync(new ChatCompletionRequest
            {
                Model = node.Model,
                Messages =
                [
                    ChatMessages.System(AgentPrompts.MemoryExtractor),
                    ChatMessages.User(BuildInput(thread, plan, execution, finalText, existing))
                ],
                Temperature = 0.2,
                MaxTokens = Math.Min(node.MaxTokens, 4096)
            }, cancellationToken: cancellationToken);

            var memories = Parse(result.Content);
            var saved = new List<MemoryItemDto>();
            foreach (var (type, label, description) in memories.Take(MaxMemories))
            {
                // Without a registered project only knowledge memory can be kept.
                var effectiveType = thread.ProjectId is null ? MemoryType.Knowledge : type;
                saved.Add(await memoryService.AddShortAsync(new SaveMemoryRequest(effectiveType, thread.ProjectId, label, description), thread.Id, cancellationToken));
            }

            if (saved.Count > 0)
            {
                var shortCount = saved.Count(m => m.Level == MemoryLevel.Short);
                activityLog.Info("memory",
                    $"{saved.Count} memory baru dari thread ini ({shortCount} short/Redis, {saved.Count - shortCount} long/DB).",
                    thread.Id, thread.ProjectId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Memory extraction failed for thread {ThreadId}", thread.Id);
            activityLog.Warn("memory", $"Ekstraksi memory gagal: {ex.Message}", thread.Id, thread.ProjectId);
        }
    }

    private static string BuildInput(ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution, string finalText, IReadOnlyList<MemoryIndexEntry> existing)
    {
        var builder = new StringBuilder();
        builder.Append("# Developer request\n").Append(thread.Prompt).Append("\n\n");
        builder.Append("# Plan summary\n").Append(plan.Summary).Append("\n\n");
        if (plan.Contract.Count > 0)
        {
            builder.Append("# Contract\n");
            foreach (var item in plan.Contract) builder.Append($"- {item.Name}: {item.Value}\n");
            builder.Append('\n');
        }
        if (execution is not null)
        {
            builder.Append("# Task results\n");
            foreach (var r in execution.Results) builder.Append($"- {r.TaskKey} {r.File} [{r.Status}]: {r.Summary ?? r.Error}\n");
            builder.Append('\n');
        }
        builder.Append("# Final reply to the developer\n").Append(finalText.Length > 6000 ? finalText[..6000] : finalText).Append("\n\n");
        builder.Append("# Existing memory labels (do not duplicate)\n");
        builder.Append(existing.Count == 0 ? "(none)\n" : string.Concat(existing.Select(e => $"- {e.Label}\n")));
        return builder.ToString();
    }

    private static List<(MemoryType Type, string Label, string Description)> Parse(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return [];

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content[start..(end + 1)]);
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }

        var list = new List<(MemoryType, string, string)>();
        foreach (var item in root?["memories"] as JsonArray ?? [])
        {
            var label = Text(item?["label"])?.Trim();
            var description = Text(item?["description"])?.Trim();
            var typeText = Text(item?["type"]);
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(description)) continue;

            var type = string.Equals(typeText, "knowledge", StringComparison.OrdinalIgnoreCase) ? MemoryType.Knowledge : MemoryType.Project;
            list.Add((type, label.Length > 300 ? label[..300] : label, description));
        }
        return list;
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
