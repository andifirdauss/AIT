using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Testing;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    // The test accounts and testing settings of a file: the same account (by id, else by project and username in any
    // case) and the same project's settings are updated only when the file's copy is newer.
    private async Task<TestingImport> ImportTestingAsync(TestingExport? testing, bool dryRun, CancellationToken cancellationToken)
    {
        if (testing is null || (testing.Accounts?.Count ?? 0) + (testing.Settings?.Count ?? 0) + (testing.Scenarios?.Count ?? 0) == 0) return TestingImport.None;

        var warnings = new List<string>();
        var accounts = new List<(TestAccountExportItem Item, string Key, (string Username, string? Role, string? Attributes, string? Notes) Values)>();
        var items = testing.Accounts ?? [];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (string.IsNullOrWhiteSpace(item.ProjectKey))
            {
                warnings.Add(Lang.T($"Akun uji #{i + 1}: project key kosong.", $"Test account #{i + 1}: empty project key."));
                continue;
            }
            try
            {
                var attributes = item.Attributes is null ? null : new Dictionary<string, string>(item.Attributes);
                accounts.Add((item, item.ProjectKey.Trim(), TestAccountText.Validate(new SaveTestAccountRequest(item.Username, item.Role, attributes, item.Notes))));
            }
            catch (AppValidationException ex)
            {
                warnings.Add(Lang.T($"Akun uji #{i + 1}: {ex.Message}", $"Test account #{i + 1}: {ex.Message}"));
            }
        }
        var settings = (testing.Settings ?? []).Where(s => !string.IsNullOrWhiteSpace(s.ProjectKey)).ToList();

        var keys = accounts.Select(a => a.Key).Concat(settings.Select(s => s.ProjectKey.Trim())).Distinct(StringComparer.Ordinal).ToList();
        await using var db = await longStore.CreateTestingAsync(cancellationToken);
        var existing = await db.TestAccounts.Where(a => keys.Contains(a.ProjectKey)).ToListAsync(cancellationToken);
        var existingSettings = await db.TestingSettings.Where(s => keys.Contains(s.ProjectKey)).ToListAsync(cancellationToken);
        var byId = existing.ToDictionary(a => a.Id);

        int added = 0, updated = 0, skipped = 0;
        var seen = new HashSet<Guid>();
        foreach (var (item, key, values) in accounts)
        {
            if (!seen.Add(item.Id))
            {
                skipped++;
                continue;
            }
            var local = byId.GetValueOrDefault(item.Id)
                        ?? existing.FirstOrDefault(a => a.ProjectKey == key && string.Equals(a.Username, values.Username, StringComparison.OrdinalIgnoreCase));
            if (local is not null)
            {
                if (local.ProjectKey != key || (item.UpdatedAt ?? item.CreatedAt) <= (local.UpdatedAt ?? local.CreatedAt))
                {
                    skipped++;
                    continue;
                }
                if (!dryRun) Apply(local, values, item);
                updated++;
                continue;
            }
            if (!dryRun)
            {
                var account = new TestAccount { Id = item.Id, ProjectKey = key, CreatedAt = item.CreatedAt, CreatedBy = item.CreatedBy };
                Apply(account, values, item);
                db.TestAccounts.Add(account);
                existing.Add(account);
            }
            added++;
        }

        foreach (var item in settings)
        {
            var key = item.ProjectKey.Trim();
            var local = existingSettings.FirstOrDefault(s => s.ProjectKey == key);
            if (local is not null && (item.UpdatedAt ?? item.CreatedAt) <= (local.UpdatedAt ?? local.CreatedAt)) continue;
            if (dryRun) continue;
            if (local is null)
            {
                local = new TestingSettings { ProjectKey = key, CreatedAt = item.CreatedAt };
                db.TestingSettings.Add(local);
                existingSettings.Add(local);
            }
            local.DevPassword = string.IsNullOrWhiteSpace(item.DevPassword) ? null : item.DevPassword.Trim();
            local.DevPasswordSource = item.DevPasswordSource;
            local.Impersonation = TestAccountText.SerializeImpersonation(item.Impersonation);
            local.SideEffectsAcknowledgedBy = item.SideEffectsAcknowledgedBy;
            local.SideEffectsAcknowledgedAt = item.SideEffectsAcknowledgedAt;
            local.UpdatedBy = item.UpdatedBy;
            local.UpdatedAt = item.UpdatedAt;
        }

        // Scenarios: by id, else by project and slug; the newer copy wins. They refer to a thread of the exporting machine,
        // so an imported one runs only after it is made again from a thread here (its sheet keeps the steps).
        var scenarioItems = (testing.Scenarios ?? []).Where(s => !string.IsNullOrWhiteSpace(s.ProjectKey) && !string.IsNullOrWhiteSpace(s.Slug)).ToList();
        if (scenarioItems.Count > 0)
        {
            var scenarioKeys = scenarioItems.Select(s => s.ProjectKey.Trim()).Distinct(StringComparer.Ordinal).ToList();
            var local = await db.TestingScenarios.Where(s => scenarioKeys.Contains(s.ProjectKey)).ToListAsync(cancellationToken);
            foreach (var item in scenarioItems)
            {
                var key = item.ProjectKey.Trim();
                var scenarioRow = local.FirstOrDefault(s => s.Id == item.Id) ?? local.FirstOrDefault(s => s.ProjectKey == key && string.Equals(s.Slug, item.Slug, StringComparison.OrdinalIgnoreCase));
                if (scenarioRow is not null && (scenarioRow.ProjectKey != key || (item.UpdatedAt ?? item.CreatedAt) <= (scenarioRow.UpdatedAt ?? scenarioRow.CreatedAt))) continue;
                if (dryRun) continue;
                if (scenarioRow is null)
                {
                    scenarioRow = new TestingScenario { Id = item.Id, ProjectKey = key, Slug = item.Slug.Trim(), CreatedAt = item.CreatedAt, CreatedBy = item.CreatedBy };
                    db.TestingScenarios.Add(scenarioRow);
                    local.Add(scenarioRow);
                }
                scenarioRow.Title = item.Title.Length > TestingScenario.MaxTitleLength ? item.Title[..TestingScenario.MaxTitleLength] : item.Title;
                scenarioRow.Description = item.Description;
                scenarioRow.Version = item.Version;
                scenarioRow.Level = item.Level;
                scenarioRow.BaseUrl = item.BaseUrl;
                scenarioRow.Notes = item.Notes;
                scenarioRow.Steps = item.Steps;
                scenarioRow.Roles = item.Roles;
                scenarioRow.Fields = item.Fields;
                scenarioRow.UpdatedBy = item.UpdatedBy;
                scenarioRow.UpdatedAt = item.UpdatedAt;
            }
        }

        if (!dryRun) await db.SaveChangesAsync(cancellationToken);
        return new TestingImport(added, updated, skipped, warnings);

        static void Apply(TestAccount account, (string Username, string? Role, string? Attributes, string? Notes) values, TestAccountExportItem item)
        {
            account.Username = values.Username;
            account.Role = values.Role;
            account.Attributes = values.Attributes;
            account.Notes = values.Notes;
            account.UpdatedBy = item.UpdatedBy;
            account.UpdatedAt = item.UpdatedAt;
        }
    }
}
