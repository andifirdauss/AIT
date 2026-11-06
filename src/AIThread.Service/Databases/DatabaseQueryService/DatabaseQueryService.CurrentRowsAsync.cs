using System.Data.Common;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // The rows of the table now, as far as the backup covers them: every row for a whole-table backup; for a WHERE
    // backup, the rows under the backed-up keys (a row whose key the statement changed shows as gone).
    private IAsyncEnumerable<Cell[]> CurrentRowsAsync(DbConnection connection, DbTransaction transaction, DatabaseTarget database, BackupSnapshot backup,
        CancellationToken cancellationToken)
    {
        var header = backup.Header;
        return header.Scope.Kind == BackupScopeKind.Rows
            ? LookupAsync(connection, transaction, database.Provider, header, KeysOf(header, backup.ReadRowsAsync(cancellationToken), cancellationToken), cancellationToken)
            : ReadRowsAsync(connection, transaction, database.Provider, header.Columns, SqlDialect.QuoteTable(database.Provider, header.Table), null, [], cancellationToken);
    }
}
