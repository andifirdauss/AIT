using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Memory;

namespace AIThread.Service.Orchestration;

public sealed partial class MemoryExtractor
{
    private static string BuildInput(ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution, string finalText, IReadOnlyList<ChangeLogFile> changed,
        IReadOnlyList<CodeMapHit> map, IReadOnlyList<ProjectModule> modules, IReadOnlyList<MemoryItemDto> existing, string language)
    {
        var builder = new StringBuilder();
        // Decided by AIT, so a cheap model does not drift into another language.
        builder.Append("# Language\n").Append(language).Append("\n\n");
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

        builder.Append("# Changed files\n");
        if (changed.Count == 0) builder.Append("(none)\n");
        foreach (var file in changed) builder.Append($"- {file.Path} ({file.Action})\n");

        builder.Append("\n# Final reply to the developer\n").Append(finalText.Length > 6000 ? finalText[..6000] : finalText).Append("\n\n");

        builder.Append("# Code map of the files in this thread\n");
        if (map.Count == 0) builder.Append("(none)\n");
        foreach (var hit in map.OrderBy(h => h.Module).ThenBy(h => h.Path))
            builder.Append($"- {hit.Path}: {(hit.Area is null ? string.Empty : hit.Area + " · ")}{hit.Module} · {hit.Layer}\n");

        builder.Append("\n# Modules\n");
        AppendModules(builder, modules);

        builder.Append("\n# Current state of the modules\n");
        if (existing.Count == 0) builder.Append("(none)\n");
        foreach (var item in existing)
        {
            builder.Append(item.Kind == MemoryKind.Summary ? $"## {item.Module} (summary)\n" : $"## {item.Module} (entry {item.CreatedAt:yyyy-MM-dd})\n");
            builder.Append(item.Description).Append("\n\n");
        }
        return builder.ToString();
    }
}
