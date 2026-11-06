namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // The SQLite column affinity closest to a source type.
    private static string Affinity(string type)
    {
        var t = type.ToLowerInvariant();
        if (t.Contains("int") || t is "bit" or "bool" or "boolean") return "INTEGER";
        if (t.Contains("real") || t.Contains("float") || t.Contains("double")) return "REAL";
        if (t.Contains("dec") || t.Contains("numeric") || t.Contains("money") || t == "number") return "NUMERIC";
        if (t.Contains("binary") || t.Contains("blob") || t.Contains("bytea") || t.Contains("image") || t == "raw") return "BLOB";
        return "TEXT";
    }
}
