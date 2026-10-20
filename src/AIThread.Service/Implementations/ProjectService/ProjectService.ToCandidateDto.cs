using AIThread.Common.DTOs;
using AIThread.Service.Workspace;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    private static ProjectCandidateDto ToCandidateDto(ProjectRoot root, Dictionary<string, Guid> registered) => new(
        root.Name,
        root.Path,
        root.Stacks,
        registered.ContainsKey(root.Path),
        registered.TryGetValue(root.Path, out var id) ? id : null);
}
