using AIThread.Common.Enums;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // k IN (@p0, …) for one key column; (k1 = @p0 AND k2 = @p1) OR … for a composite key. Parameters in row order.
    private static string KeyPredicate(MemoryStoreProvider provider, IReadOnlyList<string> keyColumns, int rows)
    {
        if (keyColumns.Count == 1)
            return $"{SqlDialect.Quote(provider, keyColumns[0])} IN ({string.Join(", ", Enumerable.Range(0, rows).Select(n => SqlDialect.Parameter(provider, n)))})";
        return string.Join(" OR ", Enumerable.Range(0, rows).Select(r =>
            "(" + string.Join(" AND ", keyColumns.Select((k, j) => $"{SqlDialect.Quote(provider, k)} = {SqlDialect.Parameter(provider, r * keyColumns.Count + j)}")) + ")"));
    }
}
