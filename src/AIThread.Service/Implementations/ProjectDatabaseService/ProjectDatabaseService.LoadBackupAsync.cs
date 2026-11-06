using AIThread.Common.Localization;
using AIThread.Service.Databases;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    private async Task<(Common.Entities.DatabaseBackup Record, TableSnapshot Snapshot)> LoadBackupAsync(Guid backupId, CancellationToken cancellationToken)
    {
        try
        {
            return await backups.LoadAsync(backupId, cancellationToken);
        }
        catch (DatabaseToolException ex)
        {
            throw new AppValidationException(Lang.T($"Backup tidak bisa dibaca: {ex.Message}", $"The backup cannot be read: {ex.Message}"));
        }
    }
}
