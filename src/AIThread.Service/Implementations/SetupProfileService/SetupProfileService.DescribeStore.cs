using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // "SQL Server sa@db.kantor:1433/AIThreadMemory", "bawaan", "SQLite default".
    private static string DescribeStore(SetupProfileStore store)
    {
        if (store.Provider == MemoryStoreProvider.Default) return Lang.T("bawaan", "built-in");
        if (store.Provider == MemoryStoreProvider.Sqlite) return $"SQLite {Clean(store.Database) ?? "default"}";

        var provider = store.Provider switch
        {
            MemoryStoreProvider.SqlServer => "SQL Server",
            MemoryStoreProvider.PostgreSql => "PostgreSQL",
            MemoryStoreProvider.MySql => "MySQL",
            _ => store.Provider.ToString()
        };
        var user = Clean(store.Username) is { } name ? $"{name}@" : string.Empty;
        var port = store.Port is { } p ? $":{p}" : string.Empty;
        var database = Clean(store.Database) is { } db ? $"/{db}" : string.Empty;
        return $"{provider} {user}{Clean(store.Host)}{port}{database}";
    }
}
