using System.Text;
using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Service.Interfaces;
using AiThread.Service.Memory;
using AiThread.Service.Models;
using AiThread.Service.Skills;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Orchestration;

// What one extraction did: the memories saved and the tokens spent; Failed when the model or the store failed.
public sealed record MemoryExtraction(int Saved, int Tokens, bool Failed = false);

// After a thread finishes, distills reusable project/knowledge memories into short memory, each with its full label.
// The files the thread touched are mapped first (waiting for the code map model when needed), so the module and layers
// of a memory come from the code map rather than from what the model happens to call them.
public sealed class MemoryExtractor(
    NodeCaller caller,
    IMemoryService memoryService,
    IProjectService projectService,
    CodeMapper codeMapper,
    ModuleRegistry registry,
    IActivityLogService activityLog,
    ILogger<MemoryExtractor> logger)
{
    private const int MaxMemories = 5;

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
                    saved.Add(await memoryService.AddShortAsync(new SaveMemoryRequest(type, thread.ProjectId, memory.Module, kind, memory.Keywords,
                        memory.Description, layers, area, memory.OtherModules), thread.Id, cancellationToken));
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

    // Planned files, files the tasks reported, and files the prompt names. A failing code map only costs the hints.
    private async Task<IReadOnlyList<CodeMapHit>> MapFilesAsync(ProjectContext? project, ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution,
        CancellationToken cancellationToken)
    {
        if (project is null || string.IsNullOrEmpty(project.Key)) return [];
        try
        {
            var files = plan.Tasks.Select(t => t.File)
                .Concat(plan.Tasks.Select(t => t.To).OfType<string>())
                .Concat(execution?.Results.Select(r => r.File) ?? [])
                .Concat(await codeMapper.ResolveReferencesAsync(project, thread.Prompt, cancellationToken))
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(60)
                .ToList();
            return await codeMapper.MapAsync(project, files, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Code map for the memory extraction of thread {ThreadId} failed.", thread.Id);
            return [];
        }
    }

    private static string BuildInput(ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution, string finalText, IReadOnlyList<CodeMapHit> map,
        IReadOnlyList<ProjectModule> modules, IReadOnlyList<ProjectModule> knowledgeModules, IReadOnlyList<MemoryIndexEntry> existing)
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

        builder.Append("# Code map of the files in this thread\n");
        if (map.Count == 0) builder.Append("(none)\n");
        foreach (var hit in map.OrderBy(h => h.Module).ThenBy(h => h.Path))
            builder.Append($"- {hit.Path}: {(hit.Area is null ? string.Empty : hit.Area + " · ")}{hit.Module} · {hit.Layer}\n");

        builder.Append("\n# Modules\n");
        AppendModules(builder, modules);
        builder.Append("\n# Knowledge modules\n");
        AppendModules(builder, knowledgeModules);

        builder.Append("\n# Existing memories (do not duplicate)\n");
        var labeled = existing.Where(e => e.Labeled).ToList();
        if (labeled.Count == 0 && existing.Count == 0) builder.Append("(none)\n");
        foreach (var group in labeled.GroupBy(e => e.Module))
        {
            builder.Append("## ").Append(group.Key).Append('\n');
            foreach (var entry in group) builder.Append("- ").Append(entry.Label).Append('\n');
        }
        foreach (var entry in existing.Where(e => !e.Labeled && e.Type != MemoryType.Business)) builder.Append("- (unlabeled) ").Append(entry.Label).Append('\n');
        return builder.ToString();
    }

    private static void AppendModules(StringBuilder builder, IReadOnlyList<ProjectModule> modules)
    {
        if (modules.Count == 0) builder.Append("(none yet)\n");
        foreach (var module in modules.Take(200))
        {
            builder.Append("- ").Append(module.Name);
            if (!string.IsNullOrEmpty(module.Aliases)) builder.Append(" (aliases: ").Append(module.Aliases.Replace(",", ", ")).Append(')');
            if (!string.IsNullOrEmpty(module.Description)) builder.Append(": ").Append(module.Description);
            builder.Append('\n');
        }
    }

    private static List<ExtractedMemory> Parse(string content)
    {
        var root = SkillAi.ParseObject(content);
        var list = new List<ExtractedMemory>();
        foreach (var item in (root?["memories"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var description = SkillAi.Text(item["description"]);
            var module = SkillAi.Text(item["module"]);
            if (description is null || module is null || !MemoryLabels.TryParseKind(SkillAi.Text(item["kind"]), out var kind)) continue;

            var type = string.Equals(SkillAi.Text(item["type"]), "knowledge", StringComparison.OrdinalIgnoreCase) ? MemoryType.Knowledge : MemoryType.Project;
            var layers = MemoryAi.Strings(item["layers"])
                .Select(l => MemoryLabels.TryParseLayer(l, out var layer) ? layer : (MemoryLayer?)null)
                .OfType<MemoryLayer>()
                .Distinct()
                .ToList();
            var keywords = MemoryAi.Strings(item["keywords"]);
            if (keywords.Count == 0) continue;
            list.Add(new ExtractedMemory(type, module, MemoryAi.Strings(item["other_modules"]), layers, kind, keywords, description));
        }
        return list;
    }

    private sealed record ExtractedMemory(MemoryType Type, string Module, IReadOnlyList<string> OtherModules, IReadOnlyList<MemoryLayer> Layers, MemoryKind Kind,
        IReadOnlyList<string> Keywords, string Description);
}
