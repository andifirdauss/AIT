using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // Replaces the project key's environments with the list (its order is the display order). The changes seen in an
    // environment that is renamed or removed are forgotten, and its wiki pages go; they are found again for the new name.
    public async Task<IReadOnlyList<ProjectEnvironmentDto>?> SaveAsync(Guid projectId, IReadOnlyList<SaveProjectEnvironment> environments,
        CancellationToken cancellationToken = default)
    {
        if (await ProjectAsync(projectId, cancellationToken) is not var (key, _)) return null;
        if (environments.Count > MaxEnvironments)
            throw new AppValidationException(Lang.T($"Maksimal {MaxEnvironments} environment.", $"At most {MaxEnvironments} environments."));
        var items = environments.Select(e => e with { Name = (e.Name ?? string.Empty).Trim(), Ref = (e.Ref ?? string.Empty).Trim() }).ToList();
        foreach (var item in items)
        {
            if (!NamePattern().IsMatch(item.Name))
                throw new AppValidationException(Lang.T($"Nama environment '{item.Name}' tidak valid: huruf, angka, '_', '.', '-' (maks 50).",
                    $"Environment name '{item.Name}' is not valid: letters, digits, '_', '.', '-' (at most 50)."));
            if (!RefPattern().IsMatch(item.Ref) || item.Ref.Contains("..", StringComparison.Ordinal))
                throw new AppValidationException(Lang.T($"Ref '{item.Ref}' untuk {item.Name} tidak valid, mis. origin/main.",
                    $"Ref '{item.Ref}' of {item.Name} is not valid, e.g. origin/main."));
        }
        if (items.FirstOrDefault(i => string.Equals(i.Name, Business.BusinessEnvService.Working, StringComparison.OrdinalIgnoreCase)) is { } reserved)
            throw new AppValidationException(Lang.T($"Nama environment '{reserved.Name}' dipakai AIT untuk wiki kerja; pilih nama lain.",
                $"The environment name '{reserved.Name}' stands for the working wiki in AIT; pick another one."));
        if (items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new AppValidationException(Lang.T($"Nama environment '{twice.Key}' dipakai dua kali.", $"Environment name '{twice.Key}' is used twice."));

        await using (var db = await longStore.CreateGitAsync(cancellationToken))
        {
            var current = await db.ProjectEnvironments.Where(e => e.ProjectKey == key).ToListAsync(cancellationToken);
            var by = GitIdentity.UserName;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var row = current.FirstOrDefault(e => string.Equals(e.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    row = new ProjectEnvironment { Id = Guid.NewGuid(), ProjectKey = key };
                    db.ProjectEnvironments.Add(row);
                }
                else if (!string.Equals(row.Ref, item.Ref, StringComparison.Ordinal))
                {
                    // Another ref: what was seen in the old one says nothing about the new one.
                    var name = row.Name;
                    await db.ChangePresences.Where(p => p.ProjectKey == key && p.Environment == name).ExecuteDeleteAsync(cancellationToken);
                }
                row.Name = item.Name;
                row.Ref = item.Ref;
                row.Order = i + 1;
                row.IsProduction = item.IsProduction;
                row.UpdatedBy = by;
                row.UpdatedAt = DateTime.UtcNow;
            }
            foreach (var removed in current.Where(e => !items.Any(i => string.Equals(i.Name, e.Name, StringComparison.OrdinalIgnoreCase))))
            {
                var name = removed.Name;
                await db.ChangePresences.Where(p => p.ProjectKey == key && p.Environment == name).ExecuteDeleteAsync(cancellationToken);
                db.ProjectEnvironments.Remove(removed);
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        await RefreshAsync(projectId, cancellationToken);
        return await ListAsync(projectId, cancellationToken);
    }
}
