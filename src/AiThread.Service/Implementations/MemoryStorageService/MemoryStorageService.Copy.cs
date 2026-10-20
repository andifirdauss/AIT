using AiThread.Common.Entities;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private static MemoryStoreSetting Copy(MemoryStoreSetting s) => new()
    {
        Provider = s.Provider,
        Host = s.Host,
        Port = s.Port,
        Database = s.Database,
        Username = s.Username,
        PasswordProtected = s.PasswordProtected,
        Options = s.Options
    };
}
