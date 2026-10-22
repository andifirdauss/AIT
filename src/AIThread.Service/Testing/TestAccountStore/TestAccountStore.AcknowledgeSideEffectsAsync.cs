using AIThread.Common.Entities;

namespace AIThread.Service.Testing;

public sealed partial class TestAccountStore
{
    public async Task<TestingSettings> AcknowledgeSideEffectsAsync(string projectKey, string by, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateTestingAsync(cancellationToken);
        var settings = await SettingsAsync(db, projectKey, cancellationToken);
        settings.SideEffectsAcknowledgedBy = by;
        settings.SideEffectsAcknowledgedAt = DateTime.UtcNow;
        settings.UpdatedBy = by;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }
}
