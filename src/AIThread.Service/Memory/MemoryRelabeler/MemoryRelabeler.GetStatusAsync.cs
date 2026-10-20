using AIThread.Common.DTOs;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    public async Task<MemoryRelabelStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        int pending = 0, labeled = 0, review = 0;
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            pending = await db.LongMemories.CountAsync(m => m.LabelVersion == null && m.ReviewReason == null, cancellationToken);
            labeled = await db.LongMemories.CountAsync(m => m.LabelVersion != null, cancellationToken);
            review = await db.LongMemories.CountAsync(m => m.ReviewReason != null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not count memories for the relabel status");
        }
        foreach (var record in await ShortRecordsAsync())
        {
            if (record.ReviewReason is not null) review++;
            else if (record.LabelVersion is null) pending++;
            else labeled++;
        }
        return new MemoryRelabelStatusDto(_gate.CurrentCount == 0, _phase, pending, labeled, review, _lastRunAt, _lastMessage, _backupPath ?? ExistingBackup(),
            _phase is null ? null : _detail);
    }
}
