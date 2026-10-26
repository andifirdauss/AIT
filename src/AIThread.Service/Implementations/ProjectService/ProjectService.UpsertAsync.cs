using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    private async Task<Project> UpsertAsync(string rootPath, string name, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.RootPath == rootPath, cancellationToken);
        if (project is not null)
        {
            project.LastSeenAt = DateTime.UtcNow;
            var previous = ApplyKey(project);
            await db.SaveChangesAsync(cancellationToken);
            await OnKeyChangedAsync(project, previous, cancellationToken);
            return project;
        }

        project = new Project
        {
            Name = name,
            RootPath = rootPath,
            LastSeenAt = DateTime.UtcNow
        };
        ApplyKey(project);
        db.Projects.Add(project);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two sessions of the same project raced; the other one registered it.
            db.ChangeTracker.Clear();
            return await db.Projects.FirstAsync(p => p.RootPath == rootPath, cancellationToken);
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ProjectChanged, new { project.Id, action = "created" }));
        activityLog.Success("project", Lang.T($"Project baru terdaftar: {project.Name} ({rootPath}).", $"New project registered: {project.Name} ({rootPath})."), projectId: project.Id);
        return project;
    }
}
