using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    // Rows written before keys existed carry only the project id; the export names their project by the key it has
    // on this machine.
    private static MemoryExportItem ToItem(LocalProjects projects, Guid id, MemoryLevel level, MemoryType type, Guid? projectId,
        string? projectKey, string? projectName, string label, string description, Guid? sourceThreadId, DateTime createdAt,
        DateTime? promotedAt, int hitCount, DateTime? lastAccessedAt, ImportedLabel? labels, string? legacyLabel)
    {
        var local = type == MemoryType.Knowledge ? null : projects.Find(projectId);
        return new MemoryExportItem(id, level, type, type == MemoryType.Knowledge ? null : projectId,
            projectKey ?? local?.Key, projectName ?? local?.Name, label, description, sourceThreadId,
            createdAt, promotedAt, hitCount, lastAccessedAt,
            labels?.Module, labels?.Layers, labels?.Kind, labels?.Area, labels?.Keywords, legacyLabel,
            labels?.ExtraModules is { Count: > 0 } extra ? extra : null);
    }
}
