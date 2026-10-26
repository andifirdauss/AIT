using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Testing;

public sealed partial class TestAccountStore
{
    public async Task<TestingSettings> SaveDevPasswordAsync(string projectKey, string? password, string source, string by, CancellationToken cancellationToken = default)
    {
        var value = string.IsNullOrWhiteSpace(password) ? null : password.Trim();
        if (value?.Length > TestingSettings.MaxDevPasswordLength)
            throw new AppValidationException(Lang.T($"Password dev maksimal {TestingSettings.MaxDevPasswordLength} karakter.", $"The dev password is at most {TestingSettings.MaxDevPasswordLength} characters."));

        await using var db = await store.CreateTestingAsync(cancellationToken);
        var settings = await SettingsAsync(db, projectKey, cancellationToken);
        settings.DevPassword = value;
        settings.DevPasswordSource = value is null ? TestingSettings.SourceDefault : source;
        settings.UpdatedBy = by;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }
}
