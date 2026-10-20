using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Memory;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private MemoryStoreConnection ToConnection(MemoryStoreSetting s) => s.Provider == MemoryStoreProvider.Default
        ? MemoryStoreConnection.Default
        : new MemoryStoreConnection(s.Provider, s.Host, s.Port, s.Database, s.Username, Unprotect(s.PasswordProtected), s.Options);
}
