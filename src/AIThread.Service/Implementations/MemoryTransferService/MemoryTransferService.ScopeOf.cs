using AIThread.Common.Enums;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private static string? ScopeOf(LocalProjects projects, MemoryType type, string? projectKey, Guid? projectId) =>
        type == MemoryType.Knowledge ? null : projectKey ?? projects.Find(projectId)?.Key ?? projectId?.ToString();
}
