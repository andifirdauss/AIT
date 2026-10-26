using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Testing;

public sealed partial class TestAccountStore
{
    public async Task<TestingSettings> SaveImpersonationAsync(string projectKey, ImpersonationInfo? info, string by, CancellationToken cancellationToken = default)
    {
        var json = TestAccountText.SerializeImpersonation(info);
        if (json?.Length > TestingSettings.MaxImpersonationLength) throw new AppValidationException(Lang.T("Keterangan login as terlalu panjang.", "The login as description is too long."));

        await using var db = await store.CreateTestingAsync(cancellationToken);
        var settings = await SettingsAsync(db, projectKey, cancellationToken);
        settings.Impersonation = json;
        settings.UpdatedBy = by;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }
}
