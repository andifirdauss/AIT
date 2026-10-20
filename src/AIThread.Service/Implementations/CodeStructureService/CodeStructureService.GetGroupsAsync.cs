using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    public async Task<IReadOnlyList<StructureGroupDto>> GetGroupsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(projectId, false, cancellationToken);
        return snapshot.Classes.Groups.Select(g => new StructureGroupDto(g.Key, g.Name, g.IsFolder, g.Types)).ToList();
    }
}
