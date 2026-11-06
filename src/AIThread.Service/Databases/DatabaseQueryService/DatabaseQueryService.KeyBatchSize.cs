namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // Keys per statement: under the 1000 items of an Oracle IN list and the 2100 parameters of SQL Server.
    private static int KeyBatchSize(int keyColumns) => Math.Max(1, Math.Min(500, 1000 / Math.Max(1, keyColumns)));
}
