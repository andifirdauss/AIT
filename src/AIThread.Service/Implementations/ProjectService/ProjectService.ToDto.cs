using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    private ProjectDto ToDto(Project p, int threadCount, int sessionCount, int memoryCount) => new(
        p.Id, p.Name, p.RootPath, ProfileOf(p.RootPath).Stacks, p.Summary, p.CreatedAt, p.LastSeenAt,
        threadCount, sessionCount, memoryCount, Directory.Exists(p.RootPath), p.ProjectKey, p.ProjectKeySource, p.ManualKey);
}
