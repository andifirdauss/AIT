using AiThread.Common.Enums;
using AiThread.Common.Memory;

namespace AiThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    // Registry key of a memory: knowledge, its project key, or the key of its project registered here.
    private static string? KeyOf(MemoryType type, string? projectKey, Guid? projectId, IReadOnlyDictionary<Guid, (string? Key, string Name)> projects) =>
        type == MemoryType.Knowledge ? MemoryLabels.KnowledgeKey
        : !string.IsNullOrEmpty(projectKey) ? projectKey
        : projectId is { } id && projects.TryGetValue(id, out var project) && !string.IsNullOrEmpty(project.Key) ? project.Key
        : null;
}
