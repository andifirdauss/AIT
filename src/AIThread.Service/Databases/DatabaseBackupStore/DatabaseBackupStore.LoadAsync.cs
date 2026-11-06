using System.Security.Cryptography;
using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseBackupStore
{
    public async Task<(DatabaseBackup Record, TableSnapshot Snapshot)> LoadAsync(Guid backupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.DatabaseBackups.AsNoTracking().SingleOrDefaultAsync(b => b.Id == backupId, cancellationToken)
                     ?? throw new DatabaseToolException("the backup does not exist");
        if (!File.Exists(record.FilePath)) throw new DatabaseToolException($"the backup file is gone ({record.FilePath})");

        TableSnapshot snapshot;
        string stored;
        try
        {
            snapshot = Deserialize(await File.ReadAllBytesAsync(record.FilePath, cancellationToken), out stored);
        }
        catch (CryptographicException ex)
        {
            throw new DatabaseToolException("the backup file cannot be decrypted on this machine (it was made with another key)", ex);
        }
        if (stored != record.Checksum || snapshot.Checksum() != record.Checksum)
            throw new DatabaseToolException("the backup file does not match its checksum; it may have been changed");
        return (record, snapshot);
    }
}
