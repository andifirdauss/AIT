using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadRollback
{
    private async Task MarkRolledBackAsync(ThreadRun thread, IReadOnlyList<RollbackFileDto> files)
    {
        var now = DateTime.UtcNow;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Threads.Where(t => t.Id == thread.Id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, ThreadStatus.RolledBack)
                .SetProperty(t => t.RolledBackAt, now)
                .SetProperty(t => t.UpdatedAt, now));
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ThreadUpdated, new { thread.Id, Status = ThreadStatus.RolledBack, thread.PlanSummary, thread.TotalMs, thread.Error }, thread.Id));
        var restored = files.Where(f => f.Action == Restore).Select(f => f.FilePath).ToList();
        var deleted = files.Where(f => f.Action == Delete).Select(f => f.FilePath).ToList();
        var parts = new List<string>();
        if (restored.Count > 0) parts.Add($"{restored.Count} file dikembalikan ({string.Join(", ", restored)})");
        if (deleted.Count > 0) parts.Add($"{deleted.Count} file buatan thread dihapus ({string.Join(", ", deleted)})");
        activityLog.Write(ActivityLevel.Warning, "thread", $"Perubahan thread dibatalkan: {(parts.Count == 0 ? "tidak ada file yang perlu dikembalikan" : string.Join("; ", parts))}.",
            thread.Id, thread.ProjectId);
    }
}
