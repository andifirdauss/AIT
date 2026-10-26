using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestCatalog
{
    public async Task<UnitTestSettings> SaveSettingsAsync(string projectKey, SaveUnitTestSettingsRequest request, string by, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Mode) || !Enum.IsDefined(request.Authoring)) throw new AppValidationException(Lang.T("Setting unit test tidak valid.", "Invalid unit test settings."));
        if (request.TimeoutSeconds is { } timeout && (timeout < MinTimeoutSeconds || timeout > MaxTimeoutSeconds))
            throw new AppValidationException(Lang.T($"Batas waktu run test harus {MinTimeoutSeconds}–{MaxTimeoutSeconds} detik, atau kosong (ikut batas build).", $"The test run time limit must be {MinTimeoutSeconds}–{MaxTimeoutSeconds} seconds, or empty (follows the build limit)."));

        await using var db = await store.CreateUnitTestsAsync(cancellationToken);
        var settings = await db.UnitTestSettings.FirstOrDefaultAsync(s => s.ProjectKey == projectKey, cancellationToken);
        if (settings is null)
        {
            settings = new UnitTestSettings { ProjectKey = projectKey };
            db.UnitTestSettings.Add(settings);
        }
        settings.Mode = request.Mode;
        settings.Authoring = request.Authoring;
        settings.KeepSnapshots = request.KeepSnapshots;
        settings.TimeoutSeconds = request.TimeoutSeconds;
        settings.UpdatedBy = by;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // Snapshots off: what was kept goes too, the project's code must not stay in the store.
        if (!request.KeepSnapshots)
            await db.UnitTestCases.Where(t => t.ProjectKey == projectKey && (t.SourceSnapshot != null || t.MethodSnapshot != null || t.SupportFiles != null))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.SourceSnapshot, (string?)null)
                    .SetProperty(t => t.MethodSnapshot, (string?)null)
                    .SetProperty(t => t.SupportFiles, (string?)null)
                    .SetProperty(t => t.SourceHash, (string?)null), cancellationToken);
        return settings;
    }
}
