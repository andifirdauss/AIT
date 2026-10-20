using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // The default SQLite file lives in each user's own profile folder, so it travels as "default" rather than a path.
    private static SetupProfileStore ToProfileStore(MemoryStoreDto store)
    {
        var database = store.Provider == MemoryStoreProvider.Sqlite
                       && string.Equals(store.Database, MemoryStorageService.DefaultSqlitePath, StringComparison.OrdinalIgnoreCase)
            ? null
            : store.Database;
        return new SetupProfileStore(store.Provider, store.Host, store.Port, database, store.Username, store.Options);
    }
}
