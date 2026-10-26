using AIThread.Common.Localization;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    // The test accounts and testing settings follow long memory too. An account the new store already has (same id, or
    // same project and username) and a project whose settings it already has are left alone, so another developer's
    // data there wins.
    private async Task CopyTestingAsync(MemoryStoreConnection from, MemoryStoreConnection to, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            await using var source = await longStore.OpenTestingAsync(from, cancellationToken);
            var accounts = await source.TestAccounts.AsNoTracking().ToListAsync(cancellationToken);
            var settings = await source.TestingSettings.AsNoTracking().ToListAsync(cancellationToken);
            var scenarios = await source.TestingScenarios.AsNoTracking().ToListAsync(cancellationToken);
            if (accounts.Count + settings.Count + scenarios.Count == 0) return;

            await using var target = await longStore.OpenTestingAsync(to, cancellationToken);
            var existing = await target.TestAccounts.AsNoTracking().Select(a => new { a.Id, a.ProjectKey, a.Username }).ToListAsync(cancellationToken);
            var ids = existing.Select(a => a.Id).ToHashSet();
            var names = existing.Select(a => a.ProjectKey + "|" + a.Username.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
            var settingKeys = (await target.TestingSettings.AsNoTracking().Select(s => s.ProjectKey).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

            target.TestAccounts.AddRange(accounts.Where(a => !ids.Contains(a.Id) && names.Add(a.ProjectKey + "|" + a.Username.ToLowerInvariant())));
            target.TestingSettings.AddRange(settings.Where(s => settingKeys.Add(s.ProjectKey)));
            var scenarioNames = (await target.TestingScenarios.AsNoTracking().Select(s => new { s.Id, s.ProjectKey, s.Slug }).ToListAsync(cancellationToken))
                .SelectMany(s => new[] { s.Id.ToString(), s.ProjectKey + "|" + s.Slug.ToLowerInvariant() }).ToHashSet(StringComparer.Ordinal);
            target.TestingScenarios.AddRange(scenarios.Where(s => !scenarioNames.Contains(s.Id.ToString()) && scenarioNames.Add(s.ProjectKey + "|" + s.Slug.ToLowerInvariant())));
            await target.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The test accounts could not be copied to the new store.");
            warnings.Add(Lang.T($"Akun uji tidak dapat disalin (bisa dipindah lewat export/import memory): {MemoryStoreTester.ErrorMessage(ex)}", $"Test accounts cannot be copied (they can be moved with memory export/import): {MemoryStoreTester.ErrorMessage(ex)}"));
        }
    }
}
