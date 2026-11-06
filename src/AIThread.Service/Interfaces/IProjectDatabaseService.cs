using AIThread.Common.DTOs;
using AIThread.Service.Databases;

namespace AIThread.Service.Interfaces;

// The databases of a project (Project > Detail → Database): kept on this machine, password encrypted. The planner and
// tester reach them through GetTargetsAsync; the card lists, tests, edits them and restores their backups.
public interface IProjectDatabaseService
{
    Task<IReadOnlyList<ProjectDatabaseDto>> GetAllAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectDatabaseDto> CreateAsync(Guid projectId, SaveProjectDatabaseRequest request, CancellationToken cancellationToken = default);

    Task<ProjectDatabaseDto> UpdateAsync(Guid projectId, Guid id, SaveProjectDatabaseRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid projectId, Guid id, CancellationToken cancellationToken = default);

    Task<DatabaseTestResultDto> TestAsync(Guid projectId, TestProjectDatabaseRequest request, CancellationToken cancellationToken = default);

    // Decrypted, for the tools of one thread. Empty when the project has none.
    Task<IReadOnlyList<DatabaseTarget>> GetTargetsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DatabaseBackupDto>> GetBackupsAsync(Guid projectId, CancellationToken cancellationToken = default);

    // Brings the table back to a backup; commits only when it then matches the backup exactly.
    Task<DatabaseRestoreResultDto> RestoreBackupAsync(Guid projectId, Guid backupId, CancellationToken cancellationToken = default);
}
