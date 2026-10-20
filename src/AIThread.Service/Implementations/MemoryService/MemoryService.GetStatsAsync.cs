using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    public async Task<MemoryStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var shorts = await SafeShortAsync();
        var longDescription = longStore.Description;
        var longByType = new Dictionary<MemoryType, int>();
        int longUnlabeled = 0, longReview = 0;
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            longByType = await db.LongMemories.AsNoTracking()
                .GroupBy(m => m.Type)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Type, x => x.Count, cancellationToken);
            longUnlabeled = await db.LongMemories.CountAsync(m => m.LabelVersion == null || m.Module == null || m.Kind == null, cancellationToken);
            longReview = await db.LongMemories.CountAsync(m => m.ReviewReason != null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LongStoreFailed(ex, "Could not count long memories");
            longDescription += " — tidak terhubung";
        }

        var longCount = longByType.Values.Sum();
        var projectCount = shorts.Count(r => r.Type == MemoryType.Project) + longByType.GetValueOrDefault(MemoryType.Project);
        var knowledgeCount = shorts.Count(r => r.Type == MemoryType.Knowledge) + longByType.GetValueOrDefault(MemoryType.Knowledge);
        var unlabeled = shorts.Count(r => !IsLabeled(r.LabelVersion, r.Module, r.Kind)) + longUnlabeled;
        var review = shorts.Count(r => r.ReviewReason is not null) + longReview;
        return new MemoryStatsDto(shorts.Count, longCount, projectCount, knowledgeCount, shortStore.IsConnected,
            shortStore.Description, longDescription, unlabeled, review);
    }
}
