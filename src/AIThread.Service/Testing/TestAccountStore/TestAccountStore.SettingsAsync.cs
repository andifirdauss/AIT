using AIThread.Common.Entities;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Testing;

public sealed partial class TestAccountStore
{
    // The project's settings row, tracked; added when it has none yet.
    private static async Task<TestingSettings> SettingsAsync(ITestingDb db, string projectKey, CancellationToken cancellationToken)
    {
        var settings = await db.TestingSettings.FirstOrDefaultAsync(s => s.ProjectKey == projectKey, cancellationToken);
        if (settings is not null) return settings;
        settings = new TestingSettings { ProjectKey = projectKey };
        db.TestingSettings.Add(settings);
        return settings;
    }
}
