using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Data.Cache;
using AIThread.Data.Memory;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

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
                if (!test.Success) throw new AppValidationException(Lang.T($"Short memory: tidak dapat terhubung — {test.Message}", $"Short memory: cannot connect — {test.Message}"));
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
            throw new AppValidationException(Lang.T($"Short memory: tidak dapat terhubung — {MemoryStoreTester.ErrorMessage(ex)}", $"Short memory: cannot connect — {MemoryStoreTester.ErrorMessage(ex)}"));
        }
        catch
        {
            (store as IDisposable)?.Dispose();
            throw;
        }
    }
}
