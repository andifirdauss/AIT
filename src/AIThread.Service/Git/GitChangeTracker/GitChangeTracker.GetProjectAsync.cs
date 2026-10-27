using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Null when the project is not registered.
    public async Task<ProjectGitDto?> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == projectId)
            .Select(p => new { p.RootPath, p.GitFetchMinutes }).FirstOrDefaultAsync(cancellationToken);
        if (project is null) return null;

        var since = DateTime.UtcNow - TrackedFor;
        var followed = await db.Threads.CountAsync(t => t.ProjectId == projectId && t.GitCheckedAt != null && t.Status != ThreadStatus.RolledBack
                                                        && (t.CompletedAt ?? t.CreatedAt) >= since, cancellationToken);
        var repository = Directory.Exists(project.RootPath) && GitTools.IsRepository(project.RootPath);
        var hasRemote = repository && GitProcess.Lines((await GitProcess.RunAsync(project.RootPath, ["remote"], cancellationToken)).Output).Count > 0;
        var last = LastFetch(projectId);
        return new ProjectGitDto(repository, hasRemote, project.GitFetchMinutes, project.GitFetchMinutes ?? DefaultFetchMinutes, last?.At, last?.Error, followed);
    }
}
