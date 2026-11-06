using AIThread.Common.Enums;
using AIThread.Data.Memory;

namespace AIThread.Service.Databases;

// A project database ready to connect to (password decrypted). Lives in memory only, never in a log or a prompt;
// Display is the safe description (provider · host/database).
public sealed record DatabaseTarget(
    Guid Id,
    Guid ProjectId,
    string Name,
    MemoryStoreConnection Connection,
    DatabaseEnvironment Environment,
    IReadOnlyList<string> MaskedColumns,
    int MaxRows,
    bool AllowWrites,
    string? Description)
{
    public MemoryStoreProvider Provider => Connection.Provider;

    public string Display => Connection.Describe();

    public bool IsProd => Environment == DatabaseEnvironment.Prod;
}

// A table as named in a statement: Schema null when it was not given (the connection's default schema).
public sealed record TableName(string? Schema, string Name)
{
    public string Display => Schema is null ? Name : $"{Schema}.{Name}";
}

// Something AIT refuses to run or cannot run against a project database; Message is for the model and the developer.
public sealed class DatabaseToolException(string message, Exception? inner = null) : Exception(message, inner);
