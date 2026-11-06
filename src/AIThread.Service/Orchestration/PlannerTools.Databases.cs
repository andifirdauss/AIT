using System.Collections.Immutable;
using System.Text.Json.Nodes;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

// The project database tools (Project > Detail → Database): reads for the planner, its tools nodes and the tester;
// change_database for the planner only, since it may pause for the developer's permission.
public static partial class PlannerTools
{
    public const string ListDatabases = "list_databases";
    public const string DescribeTable = "describe_table";
    public const string QueryDatabase = "query_database";
    public const string ChangeDatabase = "change_database";

    // Properties, not fields: static fields of the other part of this class (NameColumn) must not depend on the order in
    // which the parts are initialized.
    public static ImmutableArray<string> Databases => [ListDatabases, DescribeTable, QueryDatabase, ChangeDatabase];

    // The reads, offered to the tester too.
    public static ImmutableArray<string> DatabaseReads => [ListDatabases, DescribeTable, QueryDatabase];

    public static IEnumerable<JsonObject> DatabaseDefinitions(bool canChange)
    {
        yield return ListDatabasesDefinition();
        yield return DescribeTableDefinition();
        yield return QueryDatabaseDefinition();
        if (canChange) yield return ChangeDatabaseDefinition();
    }

    public static JsonObject ListDatabasesDefinition() =>
        ChatMessages.Function(ListDatabases,
            "List the databases of this project that you may query: name, engine (SQL Server, PostgreSQL, MySQL, Oracle, SQLite), environment (Dev/Staging/Prod), whether changes are allowed, and which columns are masked. Write SQL in that engine's dialect.",
            Schema("""{ "type": "object", "properties": {} }"""));

    public static JsonObject DescribeTableDefinition() =>
        ChatMessages.Function(DescribeTable,
            "Columns of one table (type, nullable, primary key, identity, computed, masked) and its row count. Use it before writing SQL against a table you have not seen.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "database": { "type": "string", "description": "Name from list_databases." },
                "table": { "type": "string", "description": "Table name, with its schema when it is not the default one, e.g. \"dbo.Orders\" or \"public.orders\". Quote a name with mixed case the way the engine needs it." }
              },
              "required": ["database", "table"]
            }
            """));

    public static JsonObject QueryDatabaseDefinition() =>
        ChatMessages.Function(QueryDatabase,
            "Run ONE read-only SQL statement (SELECT, or WITH … SELECT) on a project database and get the rows as a table. It runs in a read-only transaction that is always rolled back; anything that writes is refused (use change_database). Ask for the columns you need, filter with WHERE and aggregate rather than reading whole tables. Sensitive columns come back as placeholders {{AIT_SECRET:pii-…}}: compare and count them, but never guess their values.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "database": { "type": "string", "description": "Name from list_databases." },
                "sql": { "type": "string", "description": "One SELECT statement in the database's own dialect (e.g. TOP for SQL Server, LIMIT for PostgreSQL/MySQL/SQLite, FETCH FIRST for Oracle)." },
                "max_rows": { "type": "integer", "description": "Rows to return at most (default: the database's limit, at most 500)." }
              },
              "required": ["database", "sql"]
            }
            """));

    public static JsonObject ChangeDatabaseDefinition() =>
        ChatMessages.Function(ChangeDatabase,
            "Change rows of a project database with ONE INSERT, UPDATE, DELETE or MERGE on one table that has a primary key (no CREATE/ALTER/DROP/TRUNCATE, no procedures: schema changes belong in a migration in the code). AIT first backs the table up as a restore point, runs your statement in a transaction it rolls back (dry test), restores the backup in the same transaction to prove the restore works, and only then asks the developer; it runs for real only after they allow it, and only when it affects the same number of rows as the dry test. Nothing runs if any step fails. For a Prod database the developer is offered a local rehearsal first: also write the same change for SQLite in sqlite_sql. Placeholders of masked values may be used; AIT puts the real values back before running. Change only what the request asks; read the rows first with query_database.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "database": { "type": "string", "description": "Name from list_databases." },
                "sql": { "type": "string", "description": "One INSERT/UPDATE/DELETE/MERGE in the database's own dialect, with a WHERE that selects exactly the intended rows." },
                "sqlite_sql": { "type": "string", "description": "The same change written for SQLite (no schema prefix, SQLite functions), for the local rehearsal of a Prod database. Required for Prod." },
                "reason": { "type": "string", "description": "One sentence, in the developer's language: what the change fixes (shown when the developer is asked to allow it)." }
              },
              "required": ["database", "sql", "reason"]
            }
            """));
}
