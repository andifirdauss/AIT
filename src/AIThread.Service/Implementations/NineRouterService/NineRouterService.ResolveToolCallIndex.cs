namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    // Most providers send an explicit index; some translated streams only send ids, or neither on continuation chunks.
    private static int ResolveToolCallIndex(SortedDictionary<int, ToolCallBuilder> toolCalls, int? explicitIndex, string? id)
    {
        if (explicitIndex is { } index) return index;
        if (id is not null)
        {
            foreach (var (key, builder) in toolCalls)
                if (builder.Id == id) return key;
            return toolCalls.Count == 0 ? 0 : toolCalls.Keys.Max() + 1;
        }
        return toolCalls.Count == 0 ? 0 : toolCalls.Keys.Max();
    }
}
