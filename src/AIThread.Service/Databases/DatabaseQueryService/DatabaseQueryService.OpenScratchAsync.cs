namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    private Task<DiffScratch> OpenScratchAsync(SnapshotHeader shape, CancellationToken cancellationToken) =>
        DiffScratch.CreateAsync(WorkFolder, shape, Reserve, cancellationToken);
}
