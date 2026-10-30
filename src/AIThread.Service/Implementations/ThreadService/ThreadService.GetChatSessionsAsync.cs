using AIThread.Common.Chat;
using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ThreadService
{
    // The aithread chat sessions of a project, most recently used first, from its latest threads (ChatSessionScan).
    public async Task<IReadOnlyList<ChatSessionDto>> GetChatSessionsAsync(Guid projectId, int take, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var threads = await db.Threads.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.SessionId != null && t.SessionId.StartsWith(ChatSessionKey.Prefix))
            .OrderByDescending(t => t.CreatedAt)
            .Take(ChatSessionScan)
            .Select(t => new { SessionId = t.SessionId!, t.Prompt, t.Status, t.CreatedAt })
            .ToListAsync(cancellationToken);

        return threads
            .GroupBy(t => t.SessionId)
            .Select(g =>
            {
                var turns = g.OrderBy(t => t.CreatedAt).ToList();
                return new ChatSessionDto(g.Key, SessionTitle(turns[0].Prompt), SessionTitle(turns[^1].Prompt), turns.Count, turns[0].CreatedAt,
                    turns[^1].CreatedAt, turns[^1].Status);
            })
            .OrderByDescending(s => s.LastAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToList();
    }
}
