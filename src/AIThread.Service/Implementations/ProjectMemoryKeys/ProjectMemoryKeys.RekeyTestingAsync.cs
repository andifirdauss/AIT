using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectMemoryKeys
{
    // The test accounts and testing settings move with the memory. An account whose username the new key already has is
    // dropped; settings the new key already has stay.
    private async Task RekeyTestingAsync(string oldKey, string newKey, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await longStore.CreateTestingAsync(cancellationToken);
            var taken = (await db.TestAccounts.AsNoTracking().Where(a => a.ProjectKey == newKey).Select(a => a.Username).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var account in await db.TestAccounts.Where(a => a.ProjectKey == oldKey).ToListAsync(cancellationToken))
            {
                if (!taken.Add(account.Username)) db.TestAccounts.Remove(account);
                else account.ProjectKey = newKey;
            }

            var settings = await db.TestingSettings.FirstOrDefaultAsync(s => s.ProjectKey == oldKey, cancellationToken);
            if (settings is not null)
            {
                if (await db.TestingSettings.AnyAsync(s => s.ProjectKey == newKey, cancellationToken)) db.TestingSettings.Remove(settings);
                else settings.ProjectKey = newKey;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not move the test accounts from {OldKey} to {NewKey}.", oldKey, newKey);
        }
    }
}
