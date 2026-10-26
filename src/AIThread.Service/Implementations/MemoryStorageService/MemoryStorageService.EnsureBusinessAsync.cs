using AIThread.Common.Localization;
using AIThread.Data.Memory;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

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
            throw new AppValidationException(Lang.T($"Business memory: tidak dapat terhubung — {MemoryStoreTester.ErrorMessage(ex)}", $"Business memory: cannot connect — {MemoryStoreTester.ErrorMessage(ex)}"));
        }
    }
}
