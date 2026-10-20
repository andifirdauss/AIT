namespace AIThread.Service.Orchestration;

public sealed partial class ContextKeeper
{
    // The planner's read_thread tool. Accepts the short id shown in chats and packs, or a full id.
    public async Task<string> ReadThreadAsync(Guid projectId, string id, CancellationToken cancellationToken)
    {
        id = id.Trim().Trim('`').ToLowerInvariant();
        Guid? threadId = Guid.TryParse(id, out var full) ? full : null;
        if (threadId is null && id.Length >= 8)
        {
            var known = await FindThreadsAsync(projectId, [id[..8]], cancellationToken);
            if (known.TryGetValue(id[..8], out var thread)) threadId = thread.Id;
        }
        if (threadId is null) return $"Error: no thread '{id}' in this project. Use an 8-character id from the session context.";

        var turn = (await LoadTurnsAsync(projectId, [threadId.Value], cancellationToken)).FirstOrDefault();
        return turn is null
            ? $"Error: no thread '{id}' in this project."
            : ContextPackBuilder.Describe(turn, 6_000, 8_000);
    }
}
