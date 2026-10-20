using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Business;

public sealed partial class BusinessMemoryWriter
{
    public async Task<BusinessBatchResult> WriteAsync(string projectKey, string projectName, IReadOnlyList<ThreadDigest> threads, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(cancellationToken);
        var author = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
        var warnings = new List<string>();
        var labels = threads.Select((t, i) => (Label: $"T{i + 1}", Thread: t)).ToDictionary(x => x.Label, x => x.Thread);

        List<BusinessPage> existing;
        await using (var db = await store.CreateAsync(cancellationToken))
            existing = await db.BusinessPages.AsNoTracking().Where(p => p.ProjectKey == projectKey).ToListAsync(cancellationToken);

        var plan = await RouteAsync(model, projectName, existing, labels, cancellationToken);

        int created = 0, updated = 0, images = 0;
        var failedThreads = new HashSet<Guid>();
        foreach (var target in plan)
        {
            var feeding = target.Threads.Where(labels.ContainsKey).Select(l => labels[l]).ToList();
            if (feeding.Count == 0) continue;

            var page = target.PageId is { } id ? existing.FirstOrDefault(p => p.Id == id) : null;
            if (target.PageId is not null && page is null)
            {
                // The model named a page that does not exist; treat it as a new one of the same module and title.
                page = existing.FirstOrDefault(p => string.Equals(p.Module, target.Module, StringComparison.OrdinalIgnoreCase)
                                                    && string.Equals(p.Title, target.Title, StringComparison.OrdinalIgnoreCase));
            }
            if (page is { AutoUpdate: false }) continue;

            try
            {
                var saved = await WritePageAsync(model, author, projectKey, projectName, page, target, feeding, cancellationToken);
                if (saved.Created) created++;
                else updated++;
                images += saved.Images;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Business page {Module} — {Title} could not be written.", target.Module, target.Title);
                warnings.Add($"Halaman '{target.Module} — {target.Title}' gagal ditulis: {ex.Message}");
                foreach (var thread in feeding) failedThreads.Add(thread.ThreadId);
            }
        }

        var digested = threads.Select(t => t.ThreadId).Where(id => !failedThreads.Contains(id)).ToList();
        return new BusinessBatchResult(digested, created, updated, images, warnings);
    }
}
