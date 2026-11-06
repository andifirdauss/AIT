namespace AIThread.Common.Entities;

// A restore point AIT made before changing a table (change_database): the table's rows as they were, in an encrypted
// file under %LOCALAPPDATA%\AIThread\db-backups. Nothing is executed until one exists and has been checked.
public class DatabaseBackup : BaseEntity
{
    public Guid ProjectDatabaseId { get; set; }
    public ProjectDatabase? ProjectDatabase { get; set; }

    public Guid? ThreadRunId { get; set; }

    // Table as named in the statement (schema included when it was given).
    public string Table { get; set; } = string.Empty;

    // The statement the backup was made for, secrets and sensitive values masked.
    public string Statement { get; set; } = string.Empty;

    public int Rows { get; set; }
    public long Bytes { get; set; }

    // Checksum over all rows (SHA-256), to verify the file and a restore.
    public string Checksum { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    // The statement was executed (committed) after this backup.
    public DateTime? ExecutedAt { get; set; }
    public int? AffectedRows { get; set; }

    public DateTime? RestoredAt { get; set; }
    public string? RestoredBy { get; set; }
}
