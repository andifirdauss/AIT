using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // null = the default, 0 = never fetch automatically.
    public async Task<ProjectGitDto?> SetFetchMinutesAsync(Guid projectId, int? minutes, CancellationToken cancellationToken = default)
    {
        if (minutes is < 0 or > MaxFetchMinutes)
            throw new AppValidationException(Lang.T($"Interval fetch harus 0–{MaxFetchMinutes} menit (0 = tidak pernah).",
                $"The fetch interval must be 0–{MaxFetchMinutes} minutes (0 = never)."));
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var updated = await db.Projects.Where(p => p.Id == projectId)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.GitFetchMinutes, minutes), cancellationToken);
            if (updated == 0) return null;
        }
        return await GetProjectAsync(projectId, cancellationToken);
    }
}
