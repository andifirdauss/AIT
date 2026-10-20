using AIThread.Data.Memory;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private async Task EnsureLongAsync(MemoryStoreConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await longStore.OpenAsync(connection, cancellationToken);
            await db.LongMemories.AnyAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AppValidationException($"Long memory: tidak dapat terhubung — {MemoryStoreTester.ErrorMessage(ex)}");
        }
    }
}
