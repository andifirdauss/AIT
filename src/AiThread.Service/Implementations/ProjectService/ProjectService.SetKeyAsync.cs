using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class ProjectService
{
    public async Task<ProjectDto?> SetKeyAsync(Guid id, string? key, CancellationToken cancellationToken = default)
    {
        if (key?.Trim().Length > ProjectKeyResolver.MaxKeyLength)
            throw new AppValidationException($"Project key maksimal {ProjectKeyResolver.MaxKeyLength} karakter.");
        var manualKey = ProjectKeyResolver.NormalizeKey(key);
        if (ProjectKeyResolver.IsLocal(manualKey))
            throw new AppValidationException($"Project key tidak boleh diawali '{ProjectKeyResolver.LocalPrefix}'.");

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
                activityLog.Warn("project", $"Key manual '{manualKey}' untuk project {project.Name} disimpan, tetapi {ProjectKeyResolver.OverrideFile} di repository tetap dipakai ('{project.ProjectKey}').", projectId: id);
            else
                activityLog.Info("project", $"Project key {project.Name}: {project.ProjectKey} ({KeySourceName(project.ProjectKeySource)}).", projectId: id);
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ProjectChanged, new { id, action = "updated" }));
        return (await GetAllAsync(cancellationToken)).FirstOrDefault(p => p.Id == id);
    }
}
