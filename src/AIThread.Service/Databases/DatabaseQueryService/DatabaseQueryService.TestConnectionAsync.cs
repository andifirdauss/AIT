using System.Diagnostics;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    public async Task<DatabaseTestOutcome> TestConnectionAsync(DatabaseTarget database, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var connection = await OpenAsync(database, cancellationToken);
            return new DatabaseTestOutcome(true, stopwatch.ElapsedMilliseconds, connection.ServerVersion, null);
        }
        catch (DatabaseToolException ex)
        {
            return new DatabaseTestOutcome(false, stopwatch.ElapsedMilliseconds, null, ex.InnerException?.Message ?? ex.Message);
        }
    }
}
