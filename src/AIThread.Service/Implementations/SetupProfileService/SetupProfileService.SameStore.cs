using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // Same server, database and account (passwords aside). A SQLite file without a path is the default file.
    private static bool SameStore(MemoryStoreDto current, SetupProfileStore target)
    {
        if (current.Provider != target.Provider) return false;
        if (target.Provider == MemoryStoreProvider.Default) return true;

        var database = target.Provider == MemoryStoreProvider.Sqlite && Clean(target.Database) is null
            ? MemoryStorageService.DefaultSqlitePath
            : Clean(target.Database);
        return string.Equals(Clean(current.Host), Clean(target.Host), StringComparison.OrdinalIgnoreCase)
               && current.Port == target.Port
               && string.Equals(Clean(current.Database), database, StringComparison.OrdinalIgnoreCase)
               && string.Equals(Clean(current.Username), Clean(target.Username), StringComparison.Ordinal)
               && string.Equals(Clean(current.Options), Clean(target.Options), StringComparison.Ordinal);
    }
}
