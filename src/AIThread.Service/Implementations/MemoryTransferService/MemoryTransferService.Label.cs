using AIThread.Common.Enums;
using AIThread.Common.Memory;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private static ImportedLabel? Label(int? version, string? module, string? layers, MemoryKind? kind, string? area, string? keywords, string? extraModules) =>
        version is not null && module is not null && kind is { } k
            ? new ImportedLabel(module, MemoryLabels.ParseLayers(layers), k, area, MemoryLabels.SplitCsv(keywords), MemoryLabels.SplitCsv(extraModules))
            : null;
}
