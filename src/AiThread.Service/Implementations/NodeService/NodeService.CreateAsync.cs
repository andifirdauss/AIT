using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    public async Task<NodeDto> CreateAsync(SaveNodeRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var node = new AiNode();
        Apply(node, request);
        await ValidateFallbackAsync(db, node, cancellationToken);
        await ApplySkillsAsync(db, node, request.SkillIds, cancellationToken);
        await ApplyDelegationsAsync(db, node, request.ToolDelegations, cancellationToken);
        db.Nodes.Add(node);
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { node.Id, action = "created" }));
        var names = await FallbackNamesAsync(db, node, cancellationToken);
        activityLog.Info("node", $"Node {node.Role} '{node.Name}' ditambahkan ({node.Model}){SkillNote(node)}{DelegationNote(node)}{FallbackNote(node, names)}.");
        return ToDto(node, names);
    }
}
