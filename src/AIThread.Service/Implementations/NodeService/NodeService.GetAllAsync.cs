using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    public async Task<IReadOnlyList<NodeDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var nodes = await WithDetails(db.Nodes.AsNoTracking()).ToListAsync(cancellationToken);
        var names = nodes.ToDictionary(n => n.Id, n => n.Name);
        return nodes.OrderBy(n => n.Role).ThenBy(n => n.Priority).ThenBy(n => n.Name).Select(n => ToDto(n, names)).ToList();
    }
}
