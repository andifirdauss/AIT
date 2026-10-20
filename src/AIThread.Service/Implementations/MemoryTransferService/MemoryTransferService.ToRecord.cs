using AIThread.Common.Memory;
using AIThread.Data.Cache;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private static ShortMemoryRecord ToRecord(Prepared item) =>
        new(item.Id, item.ProjectId, item.Type, item.Label, item.Description, item.SourceThreadId, item.CreatedAt, item.Key, item.Name,
            item.Labels?.Module, MemoryLabels.LayersCsv(item.Labels?.Layers), item.Labels?.Kind, item.Labels?.Area, MemoryLabels.JoinCsv(item.Labels?.Keywords),
            item.Labels is null ? null : MemoryLabels.CurrentVersion, item.LegacyLabel, ExtraModules: MemoryLabels.JoinCsv(item.Labels?.ExtraModules));
}
