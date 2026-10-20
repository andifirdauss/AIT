using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed partial class ModuleMerger
{
    // A page whose title already exists in the target module stays where it is (no duplicate page); the wiki pages
    // are never merged automatically.
    private async Task MovePagesAsync(string key, string from, string into, ModuleMergeMoves moves, CancellationToken cancellationToken)
    {
        try
        {
            await using var business = await businessStore.CreateAsync(cancellationToken);
            var pages = await business.BusinessPages.AsNoTracking().Where(p => p.ProjectKey == key && (p.Module == from || p.Module == into))
                .Select(p => new { p.Id, p.Module, p.Title }).ToListAsync(cancellationToken);
            var takenTitles = pages.Where(p => p.Module == into).Select(p => p.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var page in pages.Where(p => p.Module == from))
            {
                if (takenTitles.Contains(page.Title))
                {
                    moves.SkippedPages.Add(page.Title);
                    continue;
                }
                moves.Pages.Add(page.Id);
            }
            if (moves.Pages.Count == 0) return;
            var ids = moves.Pages;
            await business.BusinessPages.Where(p => ids.Contains(p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Module, into), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not move business pages from module {From} to {Into}.", from, into);
        }
    }
}
