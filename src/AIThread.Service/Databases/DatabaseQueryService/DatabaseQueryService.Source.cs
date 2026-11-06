using AIThread.Common.Enums;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // FROM and WHERE that select what a backup covers: the whole table, or the statement's own table reference and
    // condition (kept verbatim, so aliases and the engine's name resolution stay as in the statement).
    private static (string From, string? Where) Source(MemoryStoreProvider provider, SnapshotHeader header) => header.Scope.Kind == BackupScopeKind.Rows
        ? (header.Scope.Source!, "(\n" + header.Scope.Filter + "\n)")
        : (SqlDialect.QuoteTable(provider, header.Table), null);
}
