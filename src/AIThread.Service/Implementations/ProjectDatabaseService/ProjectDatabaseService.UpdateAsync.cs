using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    public async Task<ProjectDatabaseDto> UpdateAsync(Guid projectId, Guid id, SaveProjectDatabaseRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ProjectDatabases.SingleOrDefaultAsync(d => d.Id == id && d.ProjectId == projectId, cancellationToken)
                     ?? throw new AppValidationException(Lang.T("Database tidak ditemukan.", "Database not found."));
        Apply(entity, request);
        if (await db.ProjectDatabases.AnyAsync(d => d.ProjectId == projectId && d.Name == entity.Name && d.Id != id, cancellationToken))
            throw new AppValidationException(Lang.T($"Database bernama '{entity.Name}' sudah ada di project ini.", $"A database named '{entity.Name}' already exists in this project."));

        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }
}
