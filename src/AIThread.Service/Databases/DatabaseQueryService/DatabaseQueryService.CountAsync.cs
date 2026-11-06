using System.Data.Common;
using System.Globalization;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    private async Task<long> CountAsync(DbConnection connection, DbTransaction? transaction, string from, string? where, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = Command(connection, transaction, $"SELECT COUNT(*) FROM {from}" + (where is null ? string.Empty : $" WHERE {where}"));
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }
        catch (DbException ex)
        {
            throw new DatabaseToolException($"the rows of {from} cannot be counted: {ex.Message}", ex);
        }
    }
}
