using AIThread.Common.Entities;
using AIThread.Data.Memory;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    private static MemoryStoreConnection ToConnection(ProjectDatabase entity, string? password) =>
        new(entity.Provider, entity.Host, entity.Port, entity.Database, entity.Username, password, entity.Options);
}
