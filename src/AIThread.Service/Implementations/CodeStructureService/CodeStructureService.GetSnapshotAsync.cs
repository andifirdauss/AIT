using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    // For the function call scanner: the project's current snapshot, walked first when there is none or it is stale.
    public Task<StructureSnapshot> GetSnapshotAsync(Guid projectId, CancellationToken cancellationToken) => SnapshotAsync(projectId, false, cancellationToken);
}
