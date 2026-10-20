using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    private static bool IsLabeled(int? version, string? module, MemoryKind? kind) => version is not null && module is not null && kind is not null;
}
