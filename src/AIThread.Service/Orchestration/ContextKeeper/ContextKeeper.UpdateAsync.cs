using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class ContextKeeper
{
    // Runs after a Claude Code thread finished (or failed). Never throws: a missing pack only means the next planner
    // run falls back to the recent conversation.
    public async Task UpdateAsync(ThreadRun thread, ThreadRequest request, CancellationToken cancellationToken)
    {
        if (thread.ProjectId is not { } projectId) return;
        try
        {
            var session = request.Session;
            var prior = session?.ThreadIds.Where(id => id != thread.Id).ToList() ?? [];
            var incremental = session?.Pack is not null
                              && prior.Count > 0 && session.PackThreadId == prior[^1]
                              && (prior.Count + 1) % RebuildEvery != 0;

            List<Guid> wanted = incremental ? [thread.Id] : [.. prior.TakeLast(MaxRebuildTurns), thread.Id];
            var turns = await LoadTurnsAsync(projectId, wanted, cancellationToken);
            var current = turns.FirstOrDefault(t => t.ThreadId == thread.Id);
            if (current is null) return;

            var earlier = turns.Where(t => t.ThreadId != thread.Id).ToList();
            var previousPack = incremental ? session!.Pack : null;

            var (pack, author) = await WritePackAsync(
                ContextPackBuilder.Input(previousPack, earlier, request.ConversationContext, current),
                () => ContextPackBuilder.Fallback(previousPack, earlier, current),
                thread.Id, projectId, cancellationToken);

            // ThreadRecorder saves the whole entity on every update, so its copy must not stay without the pack.
            thread.ContextPack = pack;
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Threads.Where(t => t.Id == thread.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ContextPack, pack), cancellationToken);
            eventBus.Publish(AppEvent.Create(AppEventTypes.ThreadUpdated,
                new { thread.Id, thread.Status, thread.PlanSummary, thread.TotalMs, thread.Error }, thread.Id));

            activityLog.Debug("context",
                $"Konteks sesi {(incremental ? "diperbarui" : "disusun ulang dari " + (earlier.Count + 1) + " thread")} oleh {author} ({pack.Length} karakter).",
                thread.Id, projectId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Context keeper failed for thread {ThreadId}", thread.Id);
            activityLog.Warn("context", $"Context Keeper gagal memperbarui konteks sesi: {ex.Message}", thread.Id, projectId);
        }
    }
}
