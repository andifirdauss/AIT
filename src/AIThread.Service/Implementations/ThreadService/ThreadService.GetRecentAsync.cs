using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ThreadService
{
    // search: a whole thread id, or the short id the CLI shows (thread `1a2b3c4d`) as an id prefix; any text is also looked
    // for in the prompt and the project name. sessionId: only the threads of that session (an aithread chat resumed).
    public async Task<IReadOnlyList<ThreadSummaryDto>> GetRecentAsync(int take, Guid? projectId, string? search = null,
        CancellationToken cancellationToken = default, string? sessionId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Threads.AsNoTracking();
        if (projectId is { } id) query = query.Where(t => t.ProjectId == id);
        if (!string.IsNullOrEmpty(sessionId)) query = query.Where(t => t.SessionId == sessionId);

        if (search?.Trim() is { Length: > 0 } text)
        {
            if (Guid.TryParse(text, out var threadId))
            {
                query = query.Where(t => t.Id == threadId);
            }
            else
            {
                var pattern = $"%{LikeEscape(text)}%";
                var idPrefix = IdPrefixPattern().IsMatch(text) ? $"{text}%" : null;
                query = query.Where(t => EF.Functions.Like(t.Prompt, pattern, "\\")
                                         || (t.Project != null && EF.Functions.Like(t.Project.Name, pattern, "\\"))
                                         || (idPrefix != null && EF.Functions.Like(t.Id.ToString(), idPrefix)));
            }
        }

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(SummaryProjection)
            .ToListAsync(cancellationToken);
    }
}
