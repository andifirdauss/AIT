namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    // The memory's project as known on this machine: its own project when registered here, else (memory written by
    // another developer) this machine's clone of the same project, so the Memory menu can filter and edit it.
    private static (Guid? Id, string? Name) Owner(LocalProjects projects, Guid? projectId, string? projectKey, string? projectName)
    {
        var local = projects.Find(projectId) ?? (projectKey is null ? null : projects.FindByKey(projectKey));
        return local is null ? (projectId, projectName) : (local.Id, local.Name);
    }
}
