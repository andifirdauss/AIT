using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Full paths of the files to commit: those the thread wrote, or (sessionWide, a plan without tasks: "commit this
    // change") those the recent threads of the chat wrote and that were not undone.
    private async Task<IReadOnlyList<string>> PublishFilesAsync(ThreadRun thread, ProjectContext project, bool sessionWide, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        List<Guid> ids = [thread.Id];
        if (sessionWide && !string.IsNullOrEmpty(thread.SessionId))
        {
            ids = await db.Threads.AsNoTracking()
                .Where(t => t.SessionId == thread.SessionId && t.ProjectId == project.ProjectId && t.RolledBackAt == null
                            && (t.Status == ThreadStatus.Completed || t.Status == ThreadStatus.Failed))
                .OrderByDescending(t => t.CreatedAt)
                .Take(SessionChanges.MaxThreads)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
        }

        var paths = await db.ThreadFileSnapshots.AsNoTracking()
            .Where(s => ids.Contains(s.ThreadRunId))
            .Select(s => s.FilePath)
            .Distinct()
            .ToListAsync(cancellationToken);
        var scope = new WorkspaceScope(project.RootPath);
        return paths.Select(scope.FromStored).ToList();
    }
}
