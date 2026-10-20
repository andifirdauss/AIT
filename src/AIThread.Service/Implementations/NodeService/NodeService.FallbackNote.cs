using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    private static string FallbackNote(AiNode node, IReadOnlyDictionary<Guid, string> names) =>
        node.FallbackNodeId is { } id && names.TryGetValue(id, out var name) ? $", cadangan '{name}'" : string.Empty;
}
