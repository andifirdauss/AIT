using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    public async Task<ProjectDatabaseDto> CreateAsync(Guid projectId, SaveProjectDatabaseRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
            throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));

        var entity = new ProjectDatabase { ProjectId = projectId };
        Apply(entity, request);
        if (await db.ProjectDatabases.AnyAsync(d => d.ProjectId == projectId && d.Name == entity.Name, cancellationToken))
            throw new AppValidationException(Lang.T($"Database bernama '{entity.Name}' sudah ada di project ini.", $"A database named '{entity.Name}' already exists in this project."));

        db.ProjectDatabases.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        activityLog.Info("database", Lang.T($"Database project ditambahkan: {entity.Name} ({entity.Environment}, {ToConnection(entity, null).Describe()}).",
            $"Project database added: {entity.Name} ({entity.Environment}, {ToConnection(entity, null).Describe()})."), projectId: projectId);
        return ToDto(entity);
    }
}
