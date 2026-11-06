using System.Security.Cryptography;
using System.Text;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // The same change of the same thread on the same database, whatever whitespace the model used.
    private static string RehearsalKey(Guid threadId, PendingDatabaseChange change)
    {
        var statement = string.Join(" ", change.Sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return $"{threadId:N}|{change.DatabaseId:N}|" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(statement)));
    }
}
