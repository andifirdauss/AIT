using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Common.Memory;

namespace AiThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // Labeled: "Module · Layers · Kind — keywords"; business wiki pages and unlabeled (older) memory keep their label.
    public static string FormatIndex(IEnumerable<MemoryIndexEntry> entries) =>
        string.Concat(entries.Select(e =>
        {
            var level = e.Level.ToString().ToLowerInvariant();
            var type = e.Type.ToString().ToLowerInvariant();
            if (e.Labeled)
            {
                var keywords = e.Keywords is { Count: > 0 } list ? " — " + string.Join(", ", list) : string.Empty;
                return $"- {e.Id} [{level}/{type}, {e.CreatedAt:yyyy-MM-dd}] {MemoryLabels.Display(e.Area, e.Module!, e.Layers, e.Kind!.Value, e.ExtraModules)}{keywords}\n";
            }
            var tag = e.Type == MemoryType.Business ? type : $"{level}/{type}/unlabeled";
            return $"- {e.Id} [{tag}, {e.CreatedAt:yyyy-MM-dd}] {e.Label}\n";
        }));
}
