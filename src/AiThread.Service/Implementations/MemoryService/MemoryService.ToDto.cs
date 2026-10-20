using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Data.Cache;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    private static MemoryItemDto ToDto(ShortMemoryRecord r, LocalProjects projects)
    {
        var (projectId, projectName) = Owner(projects, r.ProjectId, r.ProjectKey, r.ProjectName);
        var labeled = IsLabeled(r.LabelVersion, r.Module, r.Kind);
        var extra = MemoryLabels.SplitCsv(r.ExtraModules);
        return new MemoryItemDto(r.Id, MemoryLevel.Short, r.Type, projectId, projectName,
            labeled ? MemoryLabels.Display(r.Area, r.Module!, MemoryLabels.ParseLayers(r.Layers), r.Kind!.Value, extra) : r.Label,
            r.Description, r.SourceThreadId, r.CreatedAt, null, 0, r.ProjectKey ?? projects.Find(r.ProjectId)?.Key,
            r.Module, MemoryLabels.ParseLayers(r.Layers), r.Kind, r.Area, MemoryLabels.SplitCsv(r.Keywords), labeled, r.LegacyLabel, r.ReviewReason, extra);
    }

    private static MemoryItemDto ToDto(LongMemory m, LocalProjects projects)
    {
        var (projectId, projectName) = Owner(projects, m.ProjectId, m.ProjectKey, m.ProjectName);
        var labeled = IsLabeled(m.LabelVersion, m.Module, m.Kind);
        var extra = MemoryLabels.SplitCsv(m.ExtraModules);
        return new MemoryItemDto(m.Id, MemoryLevel.Long, m.Type, projectId, projectName,
            labeled ? MemoryLabels.Display(m.Area, m.Module!, MemoryLabels.ParseLayers(m.Layers), m.Kind!.Value, extra) : m.Label,
            m.Description, m.SourceThreadId, m.CreatedAt, m.PromotedAt, m.HitCount, m.ProjectKey ?? projects.Find(m.ProjectId)?.Key,
            m.Module, MemoryLabels.ParseLayers(m.Layers), m.Kind, m.Area, MemoryLabels.SplitCsv(m.Keywords), labeled, m.LegacyLabel, m.ReviewReason, extra);
    }
}
