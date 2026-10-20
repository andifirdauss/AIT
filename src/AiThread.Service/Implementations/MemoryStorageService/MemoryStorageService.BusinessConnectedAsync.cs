using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private async Task<bool> BusinessConnectedAsync(CancellationToken cancellationToken)
    {
        if (!businessStore.IsExternal) return await MainDatabaseConnectedAsync(cancellationToken);
        try
        {
            await using var db = await businessStore.CreateAsync(cancellationToken);
            await db.BusinessPages.AnyAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
