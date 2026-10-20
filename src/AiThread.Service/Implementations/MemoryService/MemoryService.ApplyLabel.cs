using AiThread.Common.Entities;
using AiThread.Common.Memory;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    private static void ApplyLabel(LongMemory entity, PreparedLabel label)
    {
        entity.Label = label.Composed;
        entity.Module = label.Module;
        entity.ExtraModules = MemoryLabels.JoinCsv(label.ExtraModules);
        entity.Layers = MemoryLabels.LayersCsv(label.Layers);
        entity.Kind = label.Kind;
        entity.Area = label.Area;
        entity.Keywords = MemoryLabels.JoinCsv(label.Keywords);
        entity.LabelVersion = MemoryLabels.CurrentVersion;
        entity.ReviewReason = null;
    }
}
