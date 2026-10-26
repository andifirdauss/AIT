using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Data.Context;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    // Same role, not itself, and following the chain never comes back to this node.
    private static async Task ValidateFallbackAsync(AIThreadDbContext db, AiNode node, CancellationToken cancellationToken)
    {
        if (node.FallbackNodeId is not { } fallbackId) return;
        if (fallbackId == node.Id) throw new AppValidationException(Lang.T("Node tidak bisa menjadi cadangan untuk dirinya sendiri.", "A node cannot be its own fallback."));

        var links = await db.Nodes.AsNoTracking()
            .Select(n => new { n.Id, n.Name, n.Role, n.FallbackNodeId })
            .ToDictionaryAsync(n => n.Id, cancellationToken);
        if (!links.TryGetValue(fallbackId, out var fallback)) throw new AppValidationException(Lang.T("Node cadangan tidak ditemukan.", "Fallback node not found."));
        if (fallback.Role != node.Role)
            throw new AppValidationException(Lang.T($"Node cadangan harus ber-role {node.Role}, sedangkan '{fallback.Name}' ber-role {fallback.Role}.", $"The fallback node must have the {node.Role} role, but '{fallback.Name}' has the {fallback.Role} role."));

        var path = new List<string> { node.Name };
        var visited = new HashSet<Guid> { node.Id };
        for (Guid? current = fallbackId; current is { } id && links.TryGetValue(id, out var link); current = link.FallbackNodeId)
        {
            path.Add(link.Name);
            if (link.FallbackNodeId == node.Id || !visited.Add(id))
                throw new AppValidationException(Lang.T($"Rantai cadangan berputar: {string.Join(" → ", path)} → {node.Name}. Pilih node cadangan lain.", $"The fallback chain loops: {string.Join(" → ", path)} → {node.Name}. Choose another fallback node."));
        }
    }
}
