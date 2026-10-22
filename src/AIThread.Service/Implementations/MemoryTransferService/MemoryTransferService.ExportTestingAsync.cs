using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Testing;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    // The test accounts and testing settings of the exported projects (every project without a project filter), under
    // the same conditions as the unit test catalog.
    private async Task<TestingExport?> ExportTestingAsync(MemoryQuery filter, LocalProject? scope, CancellationToken cancellationToken)
    {
        if (!filter.IncludeTestAccounts || filter.Type == MemoryType.Knowledge || !string.IsNullOrWhiteSpace(filter.Search)) return null;
        if (scope is not null && scope.Key is null) return null;

        await using var db = await longStore.CreateTestingAsync(cancellationToken);
        var accounts = db.TestAccounts.AsNoTracking();
        var settings = db.TestingSettings.AsNoTracking();
        if (scope?.Key is { } key)
        {
            accounts = accounts.Where(a => a.ProjectKey == key);
            settings = settings.Where(s => s.ProjectKey == key);
        }
        var scenarios = db.TestingScenarios.AsNoTracking();
        if (scope?.Key is { } scenarioKey) scenarios = scenarios.Where(s => s.ProjectKey == scenarioKey);
        var accountRows = await accounts.OrderBy(a => a.ProjectKey).ThenBy(a => a.Username).ToListAsync(cancellationToken);
        var settingRows = await settings.OrderBy(s => s.ProjectKey).ToListAsync(cancellationToken);
        var scenarioRows = await scenarios.OrderBy(s => s.ProjectKey).ThenBy(s => s.Slug).ToListAsync(cancellationToken);
        if (accountRows.Count + settingRows.Count + scenarioRows.Count == 0) return null;

        return new TestingExport(
            accountRows.Select(a => new TestAccountExportItem(a.Id, a.ProjectKey, a.Username, a.Role, TestAccountText.ParseAttributes(a.Attributes), a.Notes,
                a.CreatedBy, a.UpdatedBy, a.CreatedAt, a.UpdatedAt)).ToList(),
            settingRows.Select(s => new TestingSettingsExportItem(s.ProjectKey, s.DevPassword, s.DevPasswordSource, TestAccountText.ParseImpersonation(s.Impersonation),
                s.SideEffectsAcknowledgedBy, s.SideEffectsAcknowledgedAt, s.UpdatedBy, s.CreatedAt, s.UpdatedAt)).ToList(),
            scenarioRows.Select(s => new TestingScenarioExportItem(s.Id, s.ProjectKey, s.Slug, s.Title, s.Description, s.Version, s.Level, s.BaseUrl, s.Notes,
                s.Steps, s.Roles, s.Fields, s.CreatedBy, s.UpdatedBy, s.CreatedAt, s.UpdatedAt)).ToList());
    }
}
