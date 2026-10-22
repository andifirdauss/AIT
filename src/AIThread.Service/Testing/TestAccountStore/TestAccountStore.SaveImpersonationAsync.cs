using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Testing;

public sealed partial class TestAccountStore
{
    public async Task<TestingSettings> SaveImpersonationAsync(string projectKey, ImpersonationInfo? info, string by, CancellationToken cancellationToken = default)
    {
        var json = TestAccountText.SerializeImpersonation(info);
        if (json?.Length > TestingSettings.MaxImpersonationLength) throw new AppValidationException("Keterangan login as terlalu panjang.");

        await using var db = await store.CreateTestingAsync(cancellationToken);
        var settings = await SettingsAsync(db, projectKey, cancellationToken);
        settings.Impersonation = json;
        settings.UpdatedBy = by;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }
}
