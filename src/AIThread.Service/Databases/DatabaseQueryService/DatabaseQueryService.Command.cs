using System.Data.Common;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    private DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql, params object?[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = Math.Max(5, Limits.CommandTimeoutSeconds);
        var provider = ProviderOf(connection);
        for (var i = 0; i < parameters.Length; i++) SqlDialect.AddParameter(command, provider, i, parameters[i]);
        return command;
    }
}
