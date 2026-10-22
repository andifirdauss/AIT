using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class MemoryExtractor
{
    public async Task<MemoryExtraction> ExtractAsync(AiNode node, ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution, string finalText, CancellationToken cancellationToken)
    {
        var tokens = 0;
        try
        {
            var project = thread.ProjectId is { } projectId ? await projectService.GetContextAsync(projectId, cancellationToken) : null;
            var map = await MapFilesAsync(project, thread, plan, execution, cancellationToken);
            var modules = string.IsNullOrEmpty(project?.Key) ? [] : await registry.ListAsync(project.Key, cancellationToken: cancellationToken);
            var knowledgeModules = await registry.ListAsync(MemoryLabels.KnowledgeKey, cancellationToken: cancellationToken);
            var existing = await memoryService.SearchAsync(new MemorySearch(thread.ProjectId, Modules: map.Select(h => h.Module).Distinct().ToList(),
                Text: thread.Prompt, Take: 40), cancellationToken);

            // Counts against the node's Max paralel like any other call to it (usually the wrapper node), and falls
            // back like it.
            var input = BuildInput(thread, plan, execution, finalText, map, modules, knowledgeModules, existing);
            var reply = await caller.ChatAsync(node, candidate => Task.FromResult(new ChatCompletionRequest
            {
                Model = candidate.Model,
                Messages =
                [
                    ChatMessages.System(AgentPrompts.MemoryExtractor),
                    ChatMessages.User(input)
                ],
                Temperature = 0.2,
                MaxTokens = Math.Min(candidate.MaxTokens, 4096)
            }), cancellationToken, onSwitch: s =>
            {
                activityLog.Warn("memory", $"Ekstraksi memory: `{s.From.Model}` tidak tersedia ({s.Reason}), beralih ke `{s.To.Model}`.", thread.Id, thread.ProjectId);
                return Task.CompletedTask;
            });

            tokens = reply.Result.TotalTokens;
            var saved = new List<MemoryItemDto>();
            foreach (var memory in Parse(reply.Result.Content).Take(MaxMemories))
            {
                // Without a registered project only knowledge memory can be kept.
                var type = thread.ProjectId is null ? MemoryType.Knowledge : memory.Type;
                var kind = MemoryLabels.KindAllowedFor(type, memory.Kind) ? memory.Kind : MemoryKind.Pattern;
                var memoryModules = memory.OtherModules.Prepend(memory.Module).ToList();
                var fromMap = type == MemoryType.Project ? map.Where(h => memoryModules.Contains(h.Module, StringComparer.OrdinalIgnoreCase)).ToList() : [];
                var layers = memory.Layers.Count > 0 ? memory.Layers : fromMap.Select(h => h.Layer).Distinct().ToList();
                var areas = fromMap.Select(h => h.Area).Distinct().ToList();
                var area = areas is [{ } single] ? single : null;
                try
                {
                    // Memory outlives the thread and may be shared with the team: a secret the model repeated stays masked.
                    saved.Add(await memoryService.AddShortAsync(new SaveMemoryRequest(type, thread.ProjectId, memory.Module, kind, memory.Keywords,
                        secrets.Redact(memory.Description), layers, area, memory.OtherModules), thread.Id, cancellationToken));
                }
                catch (AppValidationException ex)
                {
                    logger.LogInformation("Extracted memory skipped: {Reason}", ex.Message);
                }
            }

            if (saved.Count > 0)
            {
                var shortCount = saved.Count(m => m.Level == MemoryLevel.Short);
                activityLog.Info("memory",
                    $"{saved.Count} memory baru dari thread ini ({shortCount} short/Redis, {saved.Count - shortCount} long/DB): " +
                    string.Join("; ", saved.Select(m => m.Label)),
                    thread.Id, thread.ProjectId);
            }
            return new MemoryExtraction(saved.Count, tokens);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Memory extraction failed for thread {ThreadId}", thread.Id);
            activityLog.Warn("memory", $"Ekstraksi memory gagal: {ex.Message}", thread.Id, thread.ProjectId);
            return new MemoryExtraction(0, tokens, Failed: true);
        }
    }
}
