using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private static MemoryStoreDto ToDto(MemoryStoreSetting s) =>
        new(s.Provider, s.Host, s.Port, s.Database, s.Username, !string.IsNullOrEmpty(s.PasswordProtected), s.Options);
}
