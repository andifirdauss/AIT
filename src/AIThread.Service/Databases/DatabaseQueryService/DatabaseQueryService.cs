using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Databases;

// ADO.NET against the project databases. Every statement the model wrote has passed SqlStatement first; everything AIT
// writes itself (snapshots, restores) uses quoted names and parameters.
public sealed partial class DatabaseQueryService(IOptions<AIThreadOptions> options, ILogger<DatabaseQueryService> logger) : IDatabaseQueryService
{
    // Rows of a dry test shown as a sample of what the change does.
    private const int SampleRows = 5;

    private ProjectDatabaseOptions Limits => options.Value.Databases;

    // The schema of a table read in the transaction that changes it (columns in order, primary key marked).
    private sealed record TableSchema(TableName Table, IReadOnlyList<SnapshotColumn> Columns)
    {
        public bool HasIdentity => Columns.Any(c => c.IsIdentity && c.Writable);
    }
}
