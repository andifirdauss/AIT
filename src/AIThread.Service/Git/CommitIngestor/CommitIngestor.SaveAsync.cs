using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // Turning it on for the first time starts from now, unless a moment is given: older history is summarized only when
    // asked for (it costs tokens per commit).
    public async Task<CommitIngestDto?> SaveAsync(Guid projectId, SaveCommitIngestRequest request, CancellationToken cancellationToken = default)
    {
        if (await ProjectAsync(projectId, cancellationToken) is not { } project) return null;
        var reference = string.IsNullOrWhiteSpace(request.Ref) ? null : request.Ref.Trim();
        if (reference is not null && (reference.Length > ProjectEnvironment.MaxRefLength || reference.StartsWith('-') || reference.Contains("..", StringComparison.Ordinal)
                                      || reference.Any(c => char.IsWhiteSpace(c) || c is '~' or '^' or ':' or '?' or '*' or '[' or '\\')))
            throw new AppValidationException(Lang.T($"Ref '{reference}' tidak valid, mis. origin/develop.", $"Ref '{reference}' is not valid, e.g. origin/develop."));
        if (request.MaxPerRun is < 1 or > CommitIngestSettings.MaxMaxPerRun)
            throw new AppValidationException(Lang.T($"Commit per putaran harus 1–{CommitIngestSettings.MaxMaxPerRun}.", $"Commits per pass must be 1–{CommitIngestSettings.MaxMaxPerRun}."));

        var key = project.Key!;
        await using (var db = await longStore.CreateGitAsync(cancellationToken))
        {
            var settings = await db.CommitIngestSettings.FirstOrDefaultAsync(s => s.ProjectKey == key, cancellationToken);
            if (settings is null)
            {
                settings = new CommitIngestSettings { Id = Guid.NewGuid(), ProjectKey = key, Since = DateTime.UtcNow };
                db.CommitIngestSettings.Add(settings);
            }
            else if (request.Enabled && !settings.Enabled && request.Since is null)
            {
                // Turned on again: what was committed while it was off is not summarized after the fact.
                settings.Since = DateTime.UtcNow;
            }
            settings.Enabled = request.Enabled;
            settings.Ref = reference;
            if (request.Since is { } since) settings.Since = since.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(since, DateTimeKind.Utc) : since.ToUniversalTime();
            settings.MaxPerRun = request.MaxPerRun ?? settings.MaxPerRun;
            settings.UpdatedBy = GitIdentity.UserName;
            settings.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        return await GetAsync(projectId, cancellationToken);
    }
}
