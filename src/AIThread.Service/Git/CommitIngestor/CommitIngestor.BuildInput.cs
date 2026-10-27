using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Memory;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // The input of AgentPrompts.CommitExtractor; secrets in the diff and message are masked by the caller.
    private static string BuildInput(CommitInfo commit, string diff, string language, IReadOnlyList<CodeMapHit> map, IReadOnlyList<ProjectModule> modules,
        IReadOnlyList<MemoryItemDto> existing)
    {
        var builder = new StringBuilder();
        builder.Append("# Language\n").Append(language).Append("\n\n");
        builder.Append("# Commit\n").Append($"{commit.Sha} by {commit.Author} at {commit.CommittedAt:yyyy-MM-dd HH:mm} UTC\n")
            .Append(commit.Message.Length > MaxMessageChars ? commit.Message[..MaxMessageChars] : commit.Message).Append("\n\n");

        builder.Append("# Changed files\n");
        foreach (var file in commit.Files) builder.Append($"- {file.Path} ({(file.Removed ? ChangeLog.Deleted : ChangeLog.Modified)})\n");

        builder.Append("\n# Diff\n").Append(diff).Append("\n\n");

        builder.Append("# Code map of the changed files\n");
        if (map.Count == 0) builder.Append("(none)\n");
        foreach (var hit in map.OrderBy(h => h.Module).ThenBy(h => h.Path))
            builder.Append($"- {hit.Path}: {(hit.Area is null ? string.Empty : hit.Area + " · ")}{hit.Module} · {hit.Layer}\n");

        builder.Append("\n# Modules\n");
        if (modules.Count == 0) builder.Append("(none)\n");
        foreach (var module in modules.Where(m => m.MergedInto is null).Take(200))
            builder.Append($"- {module.Name}{(string.IsNullOrWhiteSpace(module.Aliases) ? string.Empty : $" (aliases: {module.Aliases})")}\n");

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
