using AiThread.Common.Entities;
using AiThread.Data.Context;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    // Same role, not itself, and following the chain never comes back to this node.
    private static async Task ValidateFallbackAsync(AiThreadDbContext db, AiNode node, CancellationToken cancellationToken)
    {
        if (node.FallbackNodeId is not { } fallbackId) return;
        if (fallbackId == node.Id) throw new AppValidationException("Node tidak bisa menjadi cadangan untuk dirinya sendiri.");

        var links = await db.Nodes.AsNoTracking()
            .Select(n => new { n.Id, n.Name, n.Role, n.FallbackNodeId })
            .ToDictionaryAsync(n => n.Id, cancellationToken);
        if (!links.TryGetValue(fallbackId, out var fallback)) throw new AppValidationException("Node cadangan tidak ditemukan.");
        if (fallback.Role != node.Role)
            throw new AppValidationException($"Node cadangan harus ber-role {node.Role}, sedangkan '{fallback.Name}' ber-role {fallback.Role}.");

        var path = new List<string> { node.Name };
        var visited = new HashSet<Guid> { node.Id };
        for (Guid? current = fallbackId; current is { } id && links.TryGetValue(id, out var link); current = link.FallbackNodeId)
        {
            path.Add(link.Name);
            if (link.FallbackNodeId == node.Id || !visited.Add(id))
                throw new AppValidationException($"Rantai cadangan berputar: {string.Join(" → ", path)} → {node.Name}. Pilih node cadangan lain.");
        }
    }
}
