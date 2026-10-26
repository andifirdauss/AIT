using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    public async Task<MemoryChangelogStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        int facts = 0, modules = 0, summaries = 0;
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var pending = await PendingAsync(db, withFacts: true, cancellationToken);
            facts = pending.Sum(p => p.Facts);
            modules = pending.Count(p => p.Facts > 0);
            summaries = await db.LongMemories.CountAsync(m => m.Kind == MemoryKind.Summary, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not count memories for the change log migration status");
        }

        var shortFacts = await ShortFactsAsync();
        var progress = ReadProgress();
        var waiting = facts + shortFacts;
        var overall = waiting == 0 ? 0 : Math.Max(progress?.Total ?? waiting, waiting);

        var total = _total;
        return new MemoryChangelogStatusDto(total > 0, facts, modules, shortFacts, summaries, Math.Min(_done, total), total, total > 0 ? _module : null,
            _lastRunAt, _lastMessage, _backupPath ?? ExistingBackup(), await ai.IsAvailableAsync(cancellationToken), overall, overall - waiting);
    }
}
