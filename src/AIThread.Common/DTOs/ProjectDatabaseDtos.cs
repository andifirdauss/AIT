using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

// A project database as the Project > Detail card shows it: never the password, only whether one is stored.
public record ProjectDatabaseDto(
    Guid Id,
    Guid ProjectId,
    string Name,
    MemoryStoreProvider Provider,
    string? Host,
    int? Port,
    string? Database,
    string? Username,
    bool HasPassword,
    string? Options,
    DatabaseEnvironment Environment,
    string? MaskedColumns,
    int MaxRows,
    bool AllowWrites,
    string? Description,
    string Display,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

// Password: null keeps the stored one, "" clears it.
public record SaveProjectDatabaseRequest(
    string Name,
    MemoryStoreProvider Provider,
    string? Host,
    int? Port,
    string? Database,
    string? Username,
    string? Password,
    string? Options,
    DatabaseEnvironment Environment,
    string? MaskedColumns,
    int? MaxRows,
    bool AllowWrites,
    string? Description);

// Id: test the saved database (with the request's changes, the stored password when Password is null); null tests the
// request as it is.
public record TestProjectDatabaseRequest(Guid? Id, SaveProjectDatabaseRequest Database);

public record DatabaseTestResultDto(bool Success, long? LatencyMs, string Message, string? ServerVersion = null);

public record DatabaseBackupDto(
    Guid Id,
    Guid ProjectDatabaseId,
    string DatabaseName,
    DatabaseEnvironment Environment,
    string Table,
    string Statement,
    int Rows,
    long Bytes,
    Guid? ThreadId,
    DateTime CreatedAt,
    DateTime? ExecutedAt,
    int? AffectedRows,
    DateTime? RestoredAt,
    string? RestoredBy,
    bool FileExists);

public record DatabaseRestoreResultDto(bool Success, int Updated, int Inserted, int Deleted, string Message);
