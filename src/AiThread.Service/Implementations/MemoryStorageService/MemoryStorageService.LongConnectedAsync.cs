using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private async Task<bool> LongConnectedAsync(CancellationToken cancellationToken)
    {
        if (!longStore.IsExternal) return await MainDatabaseConnectedAsync(cancellationToken);
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            await db.LongMemories.AnyAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
