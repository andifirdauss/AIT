using AIThread.Common.Enums;
using AIThread.Data.Memory;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private bool IsDefaultShortConnected()
    {
        if (shortStore.Connection.Provider == MemoryStoreProvider.Default) return shortStore.IsConnected;
        var store = shortStore.Open(MemoryStoreConnection.Default);
        return store.IsConnected;
    }
}
