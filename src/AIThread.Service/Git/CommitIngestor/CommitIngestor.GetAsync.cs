using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    public async Task<CommitIngestDto?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (await ProjectAsync(projectId, cancellationToken) is not { } project) return null;
        var key = project.Key!;
        await using var db = await longStore.CreateGitAsync(cancellationToken);
        var settings = await db.CommitIngestSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ProjectKey == key, cancellationToken);
        var counts = await db.IngestedCommits.AsNoTracking().Where(c => c.ProjectKey == key)
            .GroupBy(c => c.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var recent = await db.IngestedCommits.AsNoTracking().Where(c => c.ProjectKey == key && c.Note != "AIT")
            .OrderByDescending(c => c.CreatedAt).Take(RecentShown)
            .Select(c => new IngestedCommitDto(c.Id, c.Commit, c.Status, c.Title, c.Author, c.CommittedAt, c.ClaimedBy, c.MemoryId, c.Note, c.CreatedAt))
            .ToListAsync(cancellationToken);
        int Count(IngestStatus status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        return new CommitIngestDto(settings?.Enabled == true, settings?.Ref, await EffectiveRefAsync(db, key, settings, project.RootPath, cancellationToken),
            settings?.Since ?? DateTime.UtcNow, settings?.MaxPerRun ?? Common.Entities.CommitIngestSettings.DefaultMaxPerRun,
            Count(IngestStatus.Done), Count(IngestStatus.Skipped), Count(IngestStatus.Failed),
            recent.FirstOrDefault(c => c.Status == IngestStatus.Done)?.CreatedAt, recent);
    }
}
