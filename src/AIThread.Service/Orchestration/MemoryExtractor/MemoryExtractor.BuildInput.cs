using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Memory;

namespace AIThread.Service.Orchestration;

public sealed partial class MemoryExtractor
{
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
}
