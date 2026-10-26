using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Common.Memory;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    // F3b (and the seed of the module list): every wiki page's module resolved against the registry, its labels in
    // PascalCase. Only metadata changes, so no revision is written.
    private async Task NormalizeWikiAsync(CancellationToken cancellationToken)
    {
        List<BusinessPage> pages;
        try
        {
            await using var business = await businessStore.CreateAsync(cancellationToken);
            pages = await business.BusinessPages.AsNoTracking()
                .Select(p => new BusinessPage { Id = p.Id, ProjectKey = p.ProjectKey, Module = p.Module, Labels = p.Labels, Summary = p.Summary, Title = p.Title })
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the business wiki for label normalization.");
            return;
        }

        var changed = 0;
        foreach (var group in pages.GroupBy(p => p.ProjectKey))
        {
            var modules = await registry.ListAsync(group.Key, includeMerged: true, cancellationToken);
            foreach (var page in group)
            {
                var module = ModuleRegistry.Resolve(modules, page.Module);
                if (module is null)
                {
                    (module, var created) = await registry.EnsureAsync(group.Key, page.Module, page.Summary, cancellationToken: cancellationToken);
                    if (created) modules = await registry.ListAsync(group.Key, includeMerged: true, cancellationToken);
                }
                var labels = string.Join(", ", MemoryLabels.Keywords(MemoryLabels.SplitCsv(page.Labels)));
                if (labels.Length > 500) labels = labels[..500];
                if (module == page.Module && labels == page.Labels) continue;

                await using var business = await businessStore.CreateAsync(cancellationToken);
                var id = page.Id;
                await business.BusinessPages.Where(p => p.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Module, module).SetProperty(p => p.Labels, labels), cancellationToken);
                changed++;
            }
        }
        if (changed > 0) activityLog.Info("memory", Lang.T($"Migrasi label: modul dan label {changed} halaman wiki bisnis dinormalisasi.", $"Label migration: the module and label of {changed} business wiki pages normalized."));
    }
}
