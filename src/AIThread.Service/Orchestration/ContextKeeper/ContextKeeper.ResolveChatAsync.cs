using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class ContextKeeper
{
    // aithread chat sends no transcript: the session's threads are the ones carrying its key as SessionId. The latest
    // pack covers the turns up to its thread; the turns after it (at most MaxChatTurns) are described for the planner as
    // the conversation. Never throws: without it the prompt only runs without the earlier turns.
    public async Task<ResolvedChat?> ResolveChatAsync(Guid projectId, string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var since = DateTime.UtcNow - LookBack;
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var threads = await db.Threads.AsNoTracking()
                .Where(t => t.ProjectId == projectId && t.SessionId == sessionId && t.CreatedAt >= since)
                .OrderBy(t => t.CreatedAt)
                .Select(t => new { t.Id, HasPack = t.ContextPack != null })
                .ToListAsync(cancellationToken);
            if (threads.Count == 0) return null;

            var ids = threads.Select(t => t.Id).ToList();
            var packed = threads.FindLastIndex(t => t.HasPack);
            var packThreadId = packed >= 0 ? ids[packed] : (Guid?)null;
            var pack = packThreadId is { } withPack
                ? await db.Threads.AsNoTracking().Where(t => t.Id == withPack).Select(t => t.ContextPack).FirstOrDefaultAsync(cancellationToken)
                : null;

            var turns = await LoadTurnsAsync(projectId, ids.Skip(packed + 1).TakeLast(MaxChatTurns).ToList(), cancellationToken);
            var conversation = turns.Count == 0
                ? null
                : string.Join("\n", turns.Select(t => ContextPackBuilder.Describe(t, MaxChatRequestChars, MaxChatReplyChars)));
            return new ResolvedChat(new SessionContext(packThreadId, pack, ids), conversation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the earlier turns of chat session {SessionId}.", sessionId);
            return null;
        }
    }
}
