using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Testing;

public sealed partial class TestAccountStore
{
    public async Task DeleteAsync(string projectKey, Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateTestingAsync(cancellationToken);
        var account = await db.TestAccounts.FirstOrDefaultAsync(a => a.Id == accountId && a.ProjectKey == projectKey, cancellationToken)
                      ?? throw new AppValidationException(Lang.T("Akun uji tidak ditemukan.", "Test account not found."));
        db.TestAccounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);
    }
}
