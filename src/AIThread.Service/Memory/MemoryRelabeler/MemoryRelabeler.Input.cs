using System.Text;
using AIThread.Common.Entities;
using AIThread.Common.Memory;

namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    private static string Input(string key, string? projectName, IReadOnlyList<ProjectModule> modules, IReadOnlyList<ProjectModule> knowledgeModules,
        IReadOnlyDictionary<string, Candidate> ids, IReadOnlyDictionary<Guid, IReadOnlyList<CodeMapEntry>> hints)
    {
        var builder = new StringBuilder();
        builder.Append("# Project\n").Append(key == MemoryLabels.KnowledgeKey ? "(knowledge memory, not tied to a project)" : projectName ?? key).Append("\n\n");
        builder.Append("# Modules\n");
        AppendModules(builder, key == MemoryLabels.KnowledgeKey ? [] : modules);
        builder.Append("\n# Knowledge modules\n");
        AppendModules(builder, knowledgeModules);
        builder.Append("\n# Memories\n");
        foreach (var (shortId, candidate) in ids)
        {
            builder.Append("## ").Append(shortId).Append(" type=").Append(candidate.Key == MemoryLabels.KnowledgeKey ? "knowledge" : "project").Append('\n');
            builder.Append("old label: ").Append(candidate.Label).Append('\n');
            builder.Append("description: ").Append(candidate.Description.Length > 2000 ? candidate.Description[..2000] : candidate.Description).Append('\n');
            if (hints.TryGetValue(candidate.Id, out var list) && list.Count > 0)
                builder.Append("code map: ").Append(string.Join("; ", list.Select(h => $"{h.Path}: {h.Module} · {h.Layer}"))).Append('\n');
        }
        return builder.ToString();
    }
}
