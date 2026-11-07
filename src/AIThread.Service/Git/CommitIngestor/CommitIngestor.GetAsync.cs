using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Workspace;
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
            .Select(c => new IngestedCommitDto(c.Id, c.Commit, c.Status, c.Title, c.Author, c.CommittedAt, c.ClaimedBy, c.MemoryId, c.Note, c.CreatedAt, c.Branch))
            .ToListAsync(cancellationToken);
        int Count(IngestStatus status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        var effectiveRef = await EffectiveRefAsync(db, key, settings, project.RootPath, cancellationToken);
        string? currentBranch = null;
        if (effectiveRef is not null)
        {
            var head = await GitProcess.RunAsync(project.RootPath, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken);
            currentBranch = head.Ok && head.Output.Trim() is { Length: > 0 } name ? name : null;
        }
        // While it is off, what turning it on would take on: the card suggests it when there is something.
        var outside = settings?.Enabled != true && effectiveRef is not null
            ? await OutsideAsync(db, key, project.RootPath, effectiveRef, settings?.IgnoreWorkingBranch != true, cancellationToken)
            : (int?)null;

        return new CommitIngestDto(settings?.Enabled == true, settings?.Ref, effectiveRef,
            settings?.Since ?? DateTime.UtcNow, settings?.MaxPerRun ?? Common.Entities.CommitIngestSettings.DefaultMaxPerRun,
            Count(IngestStatus.Done), Count(IngestStatus.Skipped), Count(IngestStatus.Failed),
            recent.FirstOrDefault(c => c.Status == IngestStatus.Done)?.CreatedAt, recent,
            settings?.IgnoreWorkingBranch != true, currentBranch, outside, SuggestDays);
    }
}
