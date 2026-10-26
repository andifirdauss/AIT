using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    public async Task<ProjectDto?> SetKeyAsync(Guid id, string? key, CancellationToken cancellationToken = default)
    {
        if (key?.Trim().Length > ProjectKeyResolver.MaxKeyLength)
            throw new AppValidationException(Lang.T($"Project key maksimal {ProjectKeyResolver.MaxKeyLength} karakter.", $"The project key is at most {ProjectKeyResolver.MaxKeyLength} characters."));
        var manualKey = ProjectKeyResolver.NormalizeKey(key);
        if (ProjectKeyResolver.IsLocal(manualKey))
            throw new AppValidationException(Lang.T($"Project key tidak boleh diawali '{ProjectKeyResolver.LocalPrefix}'.", $"The project key must not start with '{ProjectKeyResolver.LocalPrefix}'."));

        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (project is null) return null;

            project.ManualKey = manualKey;
            var previous = ApplyKey(project);
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await OnKeyChangedAsync(project, previous, cancellationToken);

            if (manualKey is not null && project.ProjectKeySource == ProjectKeySource.File)
                activityLog.Warn("project", Lang.T($"Key manual '{manualKey}' untuk project {project.Name} disimpan, tetapi {ProjectKeyResolver.OverrideFile} di repository tetap dipakai ('{project.ProjectKey}').", $"Manual key '{manualKey}' for project {project.Name} saved, but {ProjectKeyResolver.OverrideFile} in the repository is still used ('{project.ProjectKey}')."), projectId: id);
            else
                activityLog.Info("project", $"Project key {project.Name}: {project.ProjectKey} ({KeySourceName(project.ProjectKeySource)}).", projectId: id);
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ProjectChanged, new { id, action = "updated" }));
        return (await GetAllAsync(cancellationToken)).FirstOrDefault(p => p.Id == id);
    }
}
