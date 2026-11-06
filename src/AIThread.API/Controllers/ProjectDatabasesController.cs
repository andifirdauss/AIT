using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// The databases of a project and their backups (Project > Detail → Database). The password is write-only: never returned.
[ApiController]
[Route("api/projects/{projectId:guid}")]
public class ProjectDatabasesController(IProjectDatabaseService databases) : ControllerBase
{
    [HttpGet("databases")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectDatabaseDto>>>> List(Guid projectId, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<ProjectDatabaseDto>>.Ok(await databases.GetAllAsync(projectId, cancellationToken));

    [HttpPost("databases")]
    public async Task<ActionResult<ApiResponse<ProjectDatabaseDto>>> Create(Guid projectId, SaveProjectDatabaseRequest request, CancellationToken cancellationToken)
    {
        var database = await databases.CreateAsync(projectId, request, cancellationToken);
        return ApiResponse<ProjectDatabaseDto>.Ok(database, Lang.T($"Database {database.Name} ditambahkan.", $"Database {database.Name} added."));
    }

    [HttpPut("databases/{databaseId:guid}")]
    public async Task<ActionResult<ApiResponse<ProjectDatabaseDto>>> Update(Guid projectId, Guid databaseId, SaveProjectDatabaseRequest request, CancellationToken cancellationToken)
    {
        var database = await databases.UpdateAsync(projectId, databaseId, request, cancellationToken);
        return ApiResponse<ProjectDatabaseDto>.Ok(database, Lang.T($"Database {database.Name} disimpan.", $"Database {database.Name} saved."));
    }

    [HttpDelete("databases/{databaseId:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid projectId, Guid databaseId, CancellationToken cancellationToken)
    {
        await databases.DeleteAsync(projectId, databaseId, cancellationToken);
        return ApiResponse<bool>.Ok(true, Lang.T("Database dihapus beserta backup-nya.", "Database deleted with its backups."));
    }

    [HttpPost("databases/test")]
    public async Task<ActionResult<ApiResponse<DatabaseTestResultDto>>> Test(Guid projectId, TestProjectDatabaseRequest request, CancellationToken cancellationToken) =>
        ApiResponse<DatabaseTestResultDto>.Ok(await databases.TestAsync(projectId, request, cancellationToken));

    [HttpGet("databases/backups")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DatabaseBackupDto>>>> Backups(Guid projectId, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<DatabaseBackupDto>>.Ok(await databases.GetBackupsAsync(projectId, cancellationToken));

    [HttpPost("databases/backups/{backupId:guid}/restore")]
    public async Task<ActionResult<ApiResponse<DatabaseRestoreResultDto>>> Restore(Guid projectId, Guid backupId, CancellationToken cancellationToken)
    {
        var result = await databases.RestoreBackupAsync(projectId, backupId, cancellationToken);
        return result.Success ? ApiResponse<DatabaseRestoreResultDto>.Ok(result, result.Message) : StatusCode(StatusCodes.Status409Conflict, ApiResponse<DatabaseRestoreResultDto>.Fail(result.Message));
    }
}
