using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    // Its backups go too (records cascade; files are removed here).
    public async Task DeleteAsync(Guid projectId, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ProjectDatabases.SingleOrDefaultAsync(d => d.Id == id && d.ProjectId == projectId, cancellationToken)
                     ?? throw new AppValidationException(Lang.T("Database tidak ditemukan.", "Database not found."));
        var files = await db.DatabaseBackups.Where(b => b.ProjectDatabaseId == id).Select(b => b.FilePath).ToListAsync(cancellationToken);
        db.ProjectDatabases.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left for the OS; the record is gone.
            }
        }
        activityLog.Info("database", Lang.T($"Database project dihapus: {entity.Name} ({files.Count} backup ikut dihapus).",
            $"Project database removed: {entity.Name} ({files.Count} backup(s) removed with it)."), projectId: projectId);
    }
}
