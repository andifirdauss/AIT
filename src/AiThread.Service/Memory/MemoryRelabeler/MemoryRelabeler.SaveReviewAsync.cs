using AiThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    private async Task<bool> SaveReviewAsync(Candidate candidate, string reason, CancellationToken cancellationToken)
    {
        var clipped = reason.Length > 500 ? reason[..500] : reason;
        try
        {
            if (candidate.Level == MemoryLevel.Short)
            {
                if (await shortStore.GetAsync(candidate.Id) is not { } record) return false;
                await shortStore.SaveAsync(record with { ReviewReason = clipped });
                return true;
            }

            await using var db = await longStore.CreateAsync(cancellationToken);
            var id = candidate.Id;
            return await db.LongMemories.Where(m => m.Id == id && m.LabelVersion == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReviewReason, clipped), cancellationToken) > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not mark memory {Id} for review.", candidate.Id);
            return false;
        }
    }
}
