using AIThread.Common.Options;
using AIThread.Data.Context;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseBackupStore(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IDatabaseQueryService queries,
    IDataProtectionProvider dataProtection,
    IOptions<AIThreadOptions> options,
    ILogger<DatabaseBackupStore> logger) : IDatabaseBackupStore
{
    // v2: the streamed format (SnapshotFile). A v1 file (one JSON document) is reported as not restorable.
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AiThread.DatabaseBackups.v2");

    private string Folder => Path.Combine(
        string.IsNullOrWhiteSpace(options.Value.Databases.Directory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread")
            : options.Value.Databases.Directory,
        "db-backups");
}

// A backup just made: its record, the file, and how long it took (shown on the developer's card).
public sealed record StoredBackup(Common.Entities.DatabaseBackup Record, BackupSnapshot Snapshot, TimeSpan Elapsed);
