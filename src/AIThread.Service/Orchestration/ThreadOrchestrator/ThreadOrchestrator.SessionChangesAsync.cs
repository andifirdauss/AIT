using AIThread.Common.Enums;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // What the earlier threads of this chat changed, per module: the last ten that wrote files (the file journal), not
    // the ones that only answered or read, nor the rolled-back ones, nor those whose change is no longer in the working
    // tree: thrown away outside AIT, or left on another branch (git is asked first, so a discard or checkout made a
    // moment ago counts). Never fails the thread.
    private async Task<IReadOnlyList<SessionModuleChange>> SessionChangesAsync(ProjectContext project, SessionContext? session, Guid currentThreadId,
        CancellationToken cancellationToken)
    {
        var ids = session?.ThreadIds.Where(id => id != currentThreadId).ToList() ?? [];
        if (ids.Count == 0) return [];
        try
        {
            await gitChanges.RefreshThreadsAsync(ids, cancellationToken);
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var threads = await db.Threads.AsNoTracking()
                .Where(t => ids.Contains(t.Id) && t.ProjectId == project.ProjectId && t.Status != ThreadStatus.RolledBack
                            && t.GitState != GitChangeState.Discarded && t.GitState != GitChangeState.OnOtherBranch)
                .Select(t => new { t.Id, t.Prompt, t.CreatedAt, t.CompletedAt })
                .ToListAsync(cancellationToken);
            var files = (await db.ThreadFileSnapshots.AsNoTracking()
                    .Where(s => ids.Contains(s.ThreadRunId))
                    .Select(s => new { s.ThreadRunId, s.FilePath })
                    .ToListAsync(cancellationToken))
                .GroupBy(s => s.ThreadRunId)
                .ToDictionary(g => g.Key, g => g.Select(s => s.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

            var changed = threads
                .Where(t => files.ContainsKey(t.Id))
                .Select(t => new SessionChangeThread(t.Id, t.Prompt, t.CompletedAt ?? t.CreatedAt, files[t.Id]))
                .OrderByDescending(t => t.At)
                .Take(SessionChanges.MaxThreads)
                .ToList();
            if (changed.Count == 0) return [];

            var modules = (await codeMapper.MapQuicklyAsync(project, changed.SelectMany(t => t.Files).Distinct(StringComparer.OrdinalIgnoreCase), cancellationToken))
                .GroupBy(h => h.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Module, StringComparer.OrdinalIgnoreCase);
            return SessionChanges.ByModule(changed, modules);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The changes of the session's earlier threads could not be read.");
            return [];
        }
    }
}
