using AiThread.Common.Enums;
using AiThread.Data.Cache;
using AiThread.Data.Memory;
using AiThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

// Keeps the ProjectKey of stored memory in line with the projects registered on this machine. The stores may be
// shared by several developers, so this only ever touches rows this machine owns (its project ids, or a local:
// key that no other machine can have) unless the user explicitly asks for more.
public sealed partial class ProjectMemoryKeys(
    IShortMemoryStore shortStore,
    LongMemoryStore longStore,
    ILogger<ProjectMemoryKeys> logger) : IProjectMemoryKeys
{
    // A store is shared when it is an external database or server; the built-in ones live on this machine only.
    public bool ShortStoreIsShared => shortStore is SwitchableShortMemoryStore { Connection.Provider: not MemoryStoreProvider.Default };

    public bool LongStoreIsShared => longStore.IsExternal;
}
