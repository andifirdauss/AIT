using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    private async Task RollbackQuietlyAsync(DbTransaction transaction)
    {
        try
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            // Already rolled back by the engine (an error inside the transaction), or the connection is gone.
            logger.LogDebug(ex, "Rollback of a project database transaction did not run.");
        }
    }
}
