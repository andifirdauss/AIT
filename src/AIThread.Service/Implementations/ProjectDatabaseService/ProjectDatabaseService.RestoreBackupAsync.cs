using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Databases;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    public async Task<DatabaseRestoreResultDto> RestoreBackupAsync(Guid projectId, Guid backupId, CancellationToken cancellationToken = default)
    {
        var (record, snapshot) = await LoadBackupAsync(backupId, cancellationToken);
        var target = (await GetTargetsAsync(projectId, cancellationToken)).FirstOrDefault(t => t.Id == record.ProjectDatabaseId)
                     ?? throw new AppValidationException(Lang.T("Backup ini bukan milik database project ini.", "This backup does not belong to a database of this project."));
        try
        {
            var counts = await queries.RestoreAsync(target, snapshot, cancellationToken);
            var who = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
            await backups.MarkRestoredAsync(backupId, who, cancellationToken);
            var message = Lang.T($"Tabel {record.Table} di {target.Name} dipulihkan ke backup {record.CreatedAt:yyyy-MM-dd HH:mm} UTC: {counts.Updated} diubah kembali, {counts.Inserted} disisipkan lagi, {counts.Deleted} dihapus.",
                $"Table {record.Table} on {target.Name} restored to the backup of {record.CreatedAt:yyyy-MM-dd HH:mm} UTC: {counts.Updated} changed back, {counts.Inserted} inserted again, {counts.Deleted} deleted.");
            activityLog.Success("database", message, record.ThreadRunId, projectId);
            return new DatabaseRestoreResultDto(true, counts.Updated, counts.Inserted, counts.Deleted, message);
        }
        catch (DatabaseToolException ex)
        {
            var message = Lang.T($"Restore {record.Table} di {target.Name} gagal, tidak ada yang diubah: {ex.Message}", $"Restoring {record.Table} on {target.Name} failed, nothing was changed: {ex.Message}");
            activityLog.Error("database", message, record.ThreadRunId, projectId);
            return new DatabaseRestoreResultDto(false, 0, 0, 0, message);
        }
    }
}
