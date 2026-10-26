using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Testing;

public sealed partial class TestAccountStore
{
    public async Task<TestAccount> UpdateAsync(string projectKey, Guid accountId, SaveTestAccountRequest request, string by, CancellationToken cancellationToken = default)
    {
        var (username, role, attributes, notes) = TestAccountText.Validate(request);
        await using var db = await store.CreateTestingAsync(cancellationToken);
        var account = await db.TestAccounts.FirstOrDefaultAsync(a => a.Id == accountId && a.ProjectKey == projectKey, cancellationToken)
                      ?? throw new AppValidationException(Lang.T("Akun uji tidak ditemukan.", "Test account not found."));
        var taken = await db.TestAccounts.AsNoTracking().Where(a => a.ProjectKey == projectKey && a.Id != accountId).Select(a => a.Username).ToListAsync(cancellationToken);
        if (taken.Contains(username, StringComparer.OrdinalIgnoreCase)) throw new AppValidationException(Lang.T($"Akun uji \"{username}\" sudah ada di project ini.", $"Test account \"{username}\" already exists in this project."));

        account.Username = username;
        account.Role = role;
        account.Attributes = attributes;
        account.Notes = notes;
        account.UpdatedBy = by;
        account.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return account;
    }
}
