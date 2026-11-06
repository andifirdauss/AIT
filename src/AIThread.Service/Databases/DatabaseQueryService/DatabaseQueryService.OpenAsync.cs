using System.Data.Common;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    private static async Task<DbConnection> OpenAsync(DatabaseTarget database, CancellationToken cancellationToken)
    {
        DbConnection connection;
        try
        {
            connection = SqlDialect.Create(database.Connection);
        }
        catch (ArgumentException ex)
        {
            throw new DatabaseToolException($"the connection settings of '{database.Name}' are not valid: {ex.Message}", ex);
        }

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            await connection.DisposeAsync();
            throw new DatabaseToolException($"cannot connect to '{database.Name}' ({database.Display}): {ex.Message}", ex);
        }
    }
}
