using AiThread.Common.Enums;
using AiThread.Data.Cache;
using AiThread.Data.Memory;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private async Task<IShortMemoryStore> OpenShortAsync(MemoryStoreConnection connection)
    {
        var store = shortStore.Open(connection);
        try
        {
            if (connection.Provider == MemoryStoreProvider.Redis)
            {
                var test = await MemoryStoreTester.TestAsync(MemoryLevel.Short, connection);
                if (!test.Success) throw new AppValidationException($"Short memory: tidak dapat terhubung — {test.Message}");
            }
            else
            {
                // Creates the database/table of a relational store.
                await store.GetCreatedBeforeAsync(DateTime.UnixEpoch);
            }
            return store;
        }
        catch (Exception ex) when (ex is not AppValidationException)
        {
            (store as IDisposable)?.Dispose();
            throw new AppValidationException($"Short memory: tidak dapat terhubung — {MemoryStoreTester.ErrorMessage(ex)}");
        }
        catch
        {
            (store as IDisposable)?.Dispose();
            throw;
        }
    }
}
