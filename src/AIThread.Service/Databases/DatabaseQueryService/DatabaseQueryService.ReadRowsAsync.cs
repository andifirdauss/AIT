using System.Data.Common;
using System.Runtime.CompilerServices;
using AIThread.Common.Enums;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // SELECT <columns> FROM <from> [WHERE <where>], one row at a time. The reader is closed before the caller's next
    // command on this connection, so engines without several open readers (SQL Server without MARS) are fine.
    private async IAsyncEnumerable<Cell[]> ReadRowsAsync(DbConnection connection, DbTransaction? transaction, MemoryStoreProvider provider,
        IReadOnlyList<SnapshotColumn> columns, string from, string? where, IReadOnlyList<object?> parameters, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var list = string.Join(", ", columns.Select(c => SqlDialect.Quote(provider, c.Name)));
        await using var command = Command(connection, transaction, $"SELECT {list} FROM {from}" + (where is null ? string.Empty : $" WHERE {where}"), [.. parameters]);
        DbDataReader reader;
        try
        {
            reader = await command.ExecuteReaderAsync(cancellationToken);
        }
        catch (DbException ex)
        {
            throw new DatabaseToolException($"the rows of {from} cannot be read: {ex.Message}", ex);
        }

        await using (reader)
        {
            while (true)
            {
                Cell[] row;
                try
                {
                    if (!await reader.ReadAsync(cancellationToken)) yield break;
                    row = new Cell[columns.Count];
                    for (var i = 0; i < row.Length; i++) row[i] = Cell.From(await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i));
                }
                catch (DbException ex)
                {
                    throw new DatabaseToolException($"reading the rows of {from} failed: {ex.Message}", ex);
                }
                yield return row;
            }
        }
    }
}
