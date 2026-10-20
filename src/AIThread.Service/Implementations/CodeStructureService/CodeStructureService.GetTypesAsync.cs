using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    public async Task<IReadOnlyList<StructureTypeSummaryDto>> GetTypesAsync(Guid projectId, string group, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(projectId, false, cancellationToken);
        return snapshot.Classes.TypesIn(group)
            .Select(t => new StructureTypeSummaryDto(t.Name, t.Container, t.Kind, t.Locations.Count, t.Members.Count, t.IsPartial))
            .ToList();
    }
}
