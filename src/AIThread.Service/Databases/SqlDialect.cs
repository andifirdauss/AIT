using System.Data.Common;
using AIThread.Common.Enums;
using AIThread.Data.Memory;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace AIThread.Service.Databases;

// What differs per engine: opening a connection, quoting a name, parameters, read-only transactions and catalog queries.
public static class SqlDialect
{
    public static DbConnection Create(MemoryStoreConnection connection)
    {
        var connectionString = connection.BuildConnectionString();
        return connection.Provider switch
        {
            MemoryStoreProvider.SqlServer => new SqlConnection(connectionString),
            MemoryStoreProvider.PostgreSql => new NpgsqlConnection(connectionString),
            MemoryStoreProvider.MySql => new MySqlConnection(connectionString),
            MemoryStoreProvider.Oracle => new OracleConnection(connectionString),
            MemoryStoreProvider.Sqlite => new SqliteConnection(connectionString),
            _ => throw new DatabaseToolException($"{connection.Provider} is not a relational database.")
        };
    }

    public static string Quote(MemoryStoreProvider provider, string identifier) => provider switch
    {
        MemoryStoreProvider.SqlServer => "[" + identifier.Replace("]", "]]") + "]",
        MemoryStoreProvider.MySql => "`" + identifier.Replace("`", "``") + "`",
        _ => "\"" + identifier.Replace("\"", "\"\"") + "\""
    };

    // Names are already in the engine's case (SqlStatement folds unquoted names as the engine does), so they are quoted as they are.
    public static string QuoteTable(MemoryStoreProvider provider, TableName table) =>
        (table.Schema is null ? string.Empty : Quote(provider, table.Schema) + ".") + Quote(provider, table.Name);

    // How the engine stores a name written without quotes: PostgreSQL folds to lower case, Oracle to upper case.
    public static string FoldUnquoted(MemoryStoreProvider provider, string identifier) => provider switch
    {
        MemoryStoreProvider.PostgreSql => identifier.ToLowerInvariant(),
        MemoryStoreProvider.Oracle => identifier.ToUpperInvariant(),
        _ => identifier
    };

    public static string Parameter(MemoryStoreProvider provider, int index) => provider == MemoryStoreProvider.Oracle ? $":p{index}" : $"@p{index}";

    public static void AddParameter(DbCommand command, MemoryStoreProvider provider, int index, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = provider == MemoryStoreProvider.Oracle ? $"p{index}" : $"@p{index}";
        parameter.Value = value ?? DBNull.Value;
        // Left to infer, a DateTime goes as SQL Server's old datetime (3 ms steps) or Oracle's DATE (no fractions): a
        // restored datetime2/TIMESTAMP value would then differ from the backup.
        if (value is DateTime)
        {
            if (parameter is SqlParameter sql) sql.SqlDbType = System.Data.SqlDbType.DateTime2;
            else if (parameter is OracleParameter oracleParameter) oracleParameter.OracleDbType = Oracle.ManagedDataAccess.Client.OracleDbType.TimeStamp;
        }
        command.Parameters.Add(parameter);
        if (command is OracleCommand oracle) oracle.BindByName = true;
    }

    // Makes the transaction refuse writes where the engine can (SQL Server cannot; the statement check and the
    // rollback cover it there).
    public static async Task MakeReadOnlyAsync(DbConnection connection, DbTransaction transaction, MemoryStoreProvider provider, CancellationToken cancellationToken)
    {
        var sql = provider switch
        {
            MemoryStoreProvider.PostgreSql or MemoryStoreProvider.Oracle => "SET TRANSACTION READ ONLY",
            MemoryStoreProvider.Sqlite => "PRAGMA query_only = ON",
            _ => null
        };
        if (sql is null) return;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // MySQL applies SET TRANSACTION to the next transaction, so it runs before BEGIN.
    public static async Task BeforeReadOnlyBeginAsync(DbConnection connection, MemoryStoreProvider provider, CancellationToken cancellationToken)
    {
        if (provider != MemoryStoreProvider.MySql) return;
        await using var command = connection.CreateCommand();
        command.CommandText = "SET TRANSACTION READ ONLY";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task AfterReadOnlyAsync(DbConnection connection, MemoryStoreProvider provider, CancellationToken cancellationToken)
    {
        if (provider != MemoryStoreProvider.Sqlite) return;
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only = OFF";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Primary key columns in key order. Parameters: @s/:s schema (may be null), @t/:t table.
    public static string? PrimaryKeyQuery(MemoryStoreProvider provider, TableName table) => provider switch
    {
        MemoryStoreProvider.SqlServer => """
            SELECT c.name FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.is_primary_key = 1 AND i.object_id = OBJECT_ID(@p0)
            ORDER BY ic.key_ordinal
            """,
        MemoryStoreProvider.PostgreSql => """
            SELECT a.attname FROM pg_index i
            JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            WHERE i.indrelid = CAST(@p0 AS regclass) AND i.indisprimary
            ORDER BY array_position(i.indkey, a.attnum)
            """,
        MemoryStoreProvider.MySql => """
            SELECT COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE
            WHERE TABLE_SCHEMA = COALESCE(@p1, DATABASE()) AND TABLE_NAME = @p0 AND CONSTRAINT_NAME = 'PRIMARY'
            ORDER BY ORDINAL_POSITION
            """,
        MemoryStoreProvider.Oracle => """
            SELECT cols.column_name FROM all_constraints cons
            JOIN all_cons_columns cols ON cols.owner = cons.owner AND cols.constraint_name = cons.constraint_name
            WHERE cons.constraint_type = 'P' AND cols.table_name = :p0 AND cons.owner = NVL(:p1, USER)
            ORDER BY cols.position
            """,
        MemoryStoreProvider.Sqlite => $"SELECT name FROM pragma_table_info({SqliteLiteral(table.Name)}) WHERE pk > 0 ORDER BY pk",
        _ => null
    };

    // The parameters of PrimaryKeyQuery: SQL Server and PostgreSQL take the (quoted) qualified name, MySQL and Oracle the
    // name and schema apart.
    public static IReadOnlyList<object?> PrimaryKeyParameters(MemoryStoreProvider provider, TableName table) => provider switch
    {
        MemoryStoreProvider.SqlServer or MemoryStoreProvider.PostgreSql => [QuoteTable(provider, table)],
        MemoryStoreProvider.MySql => [table.Name, table.Schema],
        MemoryStoreProvider.Oracle => [table.Name, table.Schema],
        _ => []
    };

    // MySQL tables that are not InnoDB have no transactions: a dry test there would really change the data.
    public static string? TransactionalCheck(MemoryStoreProvider provider) => provider == MemoryStoreProvider.MySql
        ? "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA = COALESCE(@p1, DATABASE()) AND TABLE_NAME = @p0"
        : null;

    // SQL Server: an explicit value for an identity column needs IDENTITY_INSERT; PostgreSQL: OVERRIDING SYSTEM VALUE.
    public static (string? Before, string? After, string InsertClause) IdentityInsert(MemoryStoreProvider provider, TableName table, bool hasIdentity)
    {
        if (!hasIdentity) return (null, null, string.Empty);
        return provider switch
        {
            MemoryStoreProvider.SqlServer => ($"SET IDENTITY_INSERT {QuoteTable(provider, table)} ON", $"SET IDENTITY_INSERT {QuoteTable(provider, table)} OFF", string.Empty),
            MemoryStoreProvider.PostgreSql => (null, null, " OVERRIDING SYSTEM VALUE"),
            _ => (null, null, string.Empty)
        };
    }

    private static string SqliteLiteral(string value) => "'" + value.Replace("'", "''") + "'";

}
