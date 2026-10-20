using AiThread.Data.Memory;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private async Task EnsureBusinessAsync(MemoryStoreConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await businessStore.OpenAsync(connection, cancellationToken);
            await db.BusinessPages.AnyAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AppValidationException($"Business memory: tidak dapat terhubung — {MemoryStoreTester.ErrorMessage(ex)}");
        }
    }
}
