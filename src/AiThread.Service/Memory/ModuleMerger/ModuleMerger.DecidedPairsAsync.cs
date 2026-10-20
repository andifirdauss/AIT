using AiThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Memory;

public sealed partial class ModuleMerger
{
    // Pairs already decided (rejected, undone, or still suggested) are not suggested again.
    private async Task<HashSet<string>> DecidedPairsAsync(string key, CancellationToken cancellationToken)
    {
        await using var catalog = await longStore.CreateCatalogAsync(cancellationToken);
        var rows = await catalog.ModuleMergeLogs.AsNoTracking()
            .Where(l => l.ProjectKey == key && l.Status != ModuleMergeStatus.Applied)
            .Select(l => new { l.FromModule, l.IntoModule }).ToListAsync(cancellationToken);
        return rows.Select(r => PairKey(r.FromModule, r.IntoModule)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
