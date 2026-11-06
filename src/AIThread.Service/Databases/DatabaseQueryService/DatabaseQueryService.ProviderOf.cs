using System.Data.Common;
using AIThread.Common.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    private static MemoryStoreProvider ProviderOf(DbConnection connection) => connection switch
    {
        SqlConnection => MemoryStoreProvider.SqlServer,
        NpgsqlConnection => MemoryStoreProvider.PostgreSql,
        MySqlConnection => MemoryStoreProvider.MySql,
        OracleConnection => MemoryStoreProvider.Oracle,
        SqliteConnection => MemoryStoreProvider.Sqlite,
        _ => MemoryStoreProvider.Default
    };
}
