using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Testing;

public sealed partial class TestAccountStore
{
    public async Task<TestAccount> AddAsync(string projectKey, SaveTestAccountRequest request, string by, CancellationToken cancellationToken = default)
    {
        var (username, role, attributes, notes) = TestAccountText.Validate(request);
        await using var db = await store.CreateTestingAsync(cancellationToken);
        var usernames = await db.TestAccounts.AsNoTracking().Where(a => a.ProjectKey == projectKey).Select(a => a.Username).ToListAsync(cancellationToken);
        if (usernames.Count >= TestAccountText.MaxAccounts) throw new AppValidationException(Lang.T($"Maksimal {TestAccountText.MaxAccounts} akun uji per project.", $"At most {TestAccountText.MaxAccounts} test accounts per project."));
        if (usernames.Contains(username, StringComparer.OrdinalIgnoreCase)) throw new AppValidationException(Lang.T($"Akun uji \"{username}\" sudah ada di project ini.", $"Test account \"{username}\" already exists in this project."));

        var account = new TestAccount
        {
            ProjectKey = projectKey,
            Username = username,
            Role = role,
            Attributes = attributes,
            Notes = notes,
            CreatedBy = by,
            UpdatedBy = by
        };
        db.TestAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
        return account;
    }
}
