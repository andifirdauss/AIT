using AiThread.Common.Enums;

namespace AiThread.Common.DTOs;

// Saved connection of one memory level. The password itself never leaves the server.
public record MemoryStoreDto(
    MemoryStoreProvider Provider,
    string? Host,
    int? Port,
    string? Database,
    string? Username,
    bool HasPassword,
    string? Options);

// Connected is the live state of the store currently in use; Description names it without secrets.
public record MemoryStoreStatusDto(bool Connected, string Description);

public record MemoryStorageSettingsDto(
    MemoryStoreDto ShortMemory,
    MemoryStoreDto LongMemory,
    MemoryStoreStatusDto ShortStatus,
    MemoryStoreStatusDto LongStatus);

// Password empty and ClearPassword false = keep the stored password.
public record SaveMemoryStoreRequest(
    MemoryStoreProvider Provider,
    string? Host,
    int? Port,
    string? Database,
    string? Username,
    string? Password,
    bool ClearPassword,
    string? Options);

// CopyExistingData copies the memories of a store being replaced into the new one; the old store is left untouched.
public record UpdateMemoryStorageRequest(
    SaveMemoryStoreRequest ShortMemory,
    SaveMemoryStoreRequest LongMemory,
    bool CopyExistingData = true);

public record MemoryStorageUpdateResultDto(
    MemoryStorageSettingsDto Settings,
    int CopiedShort,
    int CopiedLong,
    IReadOnlyList<string> Warnings);

public record TestMemoryStoreRequest(MemoryLevel Level, SaveMemoryStoreRequest Store);

public record MemoryStoreTestResultDto(bool Success, long LatencyMs, string Message);
