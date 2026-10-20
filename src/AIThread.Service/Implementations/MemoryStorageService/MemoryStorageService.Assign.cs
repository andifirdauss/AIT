using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private static void Assign(MemoryStoreSetting target, MemoryStoreSetting source)
    {
        target.Provider = source.Provider;
        target.Host = source.Host;
        target.Port = source.Port;
        target.Database = source.Database;
        target.Username = source.Username;
        target.PasswordProtected = source.PasswordProtected;
        target.Options = source.Options;
    }
}
