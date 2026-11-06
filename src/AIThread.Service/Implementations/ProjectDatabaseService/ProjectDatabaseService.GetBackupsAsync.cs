using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    public async Task<IReadOnlyList<DatabaseBackupDto>> GetBackupsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.DatabaseBackups.AsNoTracking()
            .Where(b => b.ProjectDatabase!.ProjectId == projectId)
            .OrderByDescending(b => b.CreatedAt)
            .Take(200)
            .Select(b => new { Backup = b, b.ProjectDatabase!.Name, b.ProjectDatabase.Environment })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new DatabaseBackupDto(r.Backup.Id, r.Backup.ProjectDatabaseId, r.Name, r.Environment, r.Backup.Table, r.Backup.Statement, r.Backup.Rows,
            r.Backup.Bytes, r.Backup.ThreadRunId, r.Backup.CreatedAt, r.Backup.ExecutedAt, r.Backup.AffectedRows, r.Backup.RestoredAt, r.Backup.RestoredBy,
            File.Exists(r.Backup.FilePath))).ToList();
    }
}
