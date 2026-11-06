using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// A database of a project that the planner and the tester may query (query_database) and, through the backup → dry
// test → restore test → approval flow, change (change_database). Kept on this machine only: it holds a password.
// Provider reuses the memory store providers (SQL Server, PostgreSQL, MySQL, Oracle, SQLite) and their connection builder.
public class ProjectDatabase : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    // What the model calls it ("orders-dev"); unique per project.
    public string Name { get; set; } = string.Empty;

    public MemoryStoreProvider Provider { get; set; } = MemoryStoreProvider.SqlServer;
    public string? Host { get; set; }
    public int? Port { get; set; }

    // Database name; the file path for SQLite.
    public string? Database { get; set; }
    public string? Username { get; set; }

    // Encrypted with Data Protection; never sent to a model or returned by the API.
    public string? PasswordProtected { get; set; }

    // Extra connection string keywords ("Encrypt=True;Timeout=30").
    public string? Options { get; set; }

    public DatabaseEnvironment Environment { get; set; } = DatabaseEnvironment.Dev;

    // Columns whose values the model only sees as placeholders, comma separated (on top of the names AIT detects).
    public string? MaskedColumns { get; set; }

    // Rows one query hands the model at most.
    public int MaxRows { get; set; } = 50;

    // change_database may be offered for this database at all.
    public bool AllowWrites { get; set; } = true;

    // Shown to the model: what the database holds.
    public string? Description { get; set; }
}
