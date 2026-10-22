using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.Testing;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private const int FollowUpLookback = 20;

    // The session's latest finished thread of the project that suggested that follow-up; null when none did.
    private async Task<Guid?> FollowUpThreadAsync(ThreadRequest request, int number, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.SessionId)) return null;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await db.Threads.AsNoTracking()
            .Where(t => t.SessionId == request.SessionId && t.ProjectId == request.ProjectId && t.Status == ThreadStatus.Completed && t.TestingJson != null)
            .OrderByDescending(t => t.CreatedAt)
            .Take(FollowUpLookback)
            .Select(t => new { t.Id, t.TestingJson })
            .ToListAsync(cancellationToken);
        return candidates.FirstOrDefault(t => TestFollowUpReply.Find(t.TestingJson, number) is not null)?.Id;
    }
}
