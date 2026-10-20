using AIThread.Common.Entities;

namespace AIThread.Service.Memory;

public sealed partial class MemoryAi
{
    public async Task<AiNode?> PickNodeAsync(CancellationToken cancellationToken)
    {
        foreach (var role in Roles)
            if ((await nodeService.GetActiveAsync(role, cancellationToken)).FirstOrDefault() is { } node) return node;
        return null;
    }
}
