using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Databases;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    // Tests the form as it is: with Id, the stored password fills in a password left empty.
    public async Task<DatabaseTestResultDto> TestAsync(Guid projectId, TestProjectDatabaseRequest request, CancellationToken cancellationToken = default)
    {
        var entity = new ProjectDatabase { ProjectId = projectId };
        if (request.Id is { } id)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            entity = await db.ProjectDatabases.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.ProjectId == projectId, cancellationToken)
                     ?? throw new AppValidationException(Lang.T("Database tidak ditemukan.", "Database not found."));
        }
        Apply(entity, request.Database);

        var target = new DatabaseTarget(entity.Id, projectId, entity.Name, ToConnection(entity, Unprotect(entity.PasswordProtected)), entity.Environment, [], entity.MaxRows,
            entity.AllowWrites, entity.Description);
        var outcome = await queries.TestConnectionAsync(target, cancellationToken);
        return outcome.Success
            ? new DatabaseTestResultDto(true, outcome.LatencyMs, Lang.T($"Terhubung ke {target.Display} ({outcome.LatencyMs} ms).", $"Connected to {target.Display} ({outcome.LatencyMs} ms)."), outcome.ServerVersion)
            : new DatabaseTestResultDto(false, outcome.LatencyMs, Lang.T($"Gagal terhubung ke {target.Display}: {outcome.Error}", $"Cannot connect to {target.Display}: {outcome.Error}"));
    }
}
