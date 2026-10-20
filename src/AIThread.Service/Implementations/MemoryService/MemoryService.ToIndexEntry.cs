using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Data.Cache;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    private static MemoryIndexEntry ToIndexEntry(ShortMemoryRecord r) => IsLabeled(r.LabelVersion, r.Module, r.Kind)
        ? new MemoryIndexEntry(r.Id, MemoryLevel.Short, r.Type, r.Label, r.CreatedAt, r.Module, MemoryLabels.ParseLayers(r.Layers), r.Kind, r.Area,
            MemoryLabels.SplitCsv(r.Keywords), ExtraModules: MemoryLabels.SplitCsv(r.ExtraModules))
        : new MemoryIndexEntry(r.Id, MemoryLevel.Short, r.Type, r.Label, r.CreatedAt);
}
