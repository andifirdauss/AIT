using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Databases;

// ADO.NET against the project databases. Every statement the model wrote has passed SqlStatement first; everything AIT
// writes itself (snapshots, restores) uses quoted names and parameters. Rows are streamed: a backup goes to disk as it
// is read, comparisons go through a temporary file (DiffScratch), so memory does not grow with the table.
public sealed partial class DatabaseQueryService(IOptions<AIThreadOptions> options, ILogger<DatabaseQueryService> logger) : IDatabaseQueryService
{
    // Rows of a dry test shown as a sample of what the change does.
    private const int SampleRows = 5;

    private ProjectDatabaseOptions Limits => options.Value.Databases;

    // %LOCALAPPDATA%\AIThread unless ProjectDatabaseOptions.Directory says otherwise.
    private string Root => string.IsNullOrWhiteSpace(Limits.Directory)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread")
        : Limits.Directory;

    // Comparison files of dry tests, restores and rehearsals; each is deleted when its operation ends.
    private string WorkFolder => Path.Combine(Root, "db-work");

    // Bytes every backup, comparison or rehearsal file leaves free on its disk.
    private long Reserve => Math.Max(0, Limits.BackupFreeSpaceReserveMb) * 1024L * 1024L;

    // The schema of a table read in the transaction that changes it (columns in order, primary key marked).
    private sealed record TableSchema(TableName Table, IReadOnlyList<SnapshotColumn> Columns)
    {
        public bool HasIdentity => Columns.Any(c => c.IsIdentity && c.Writable);
    }
}
