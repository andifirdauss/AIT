namespace AiThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    // Drops the snapshots of the projects not viewed for the longest time.
    private void Evict(Guid keep)
    {
        foreach (var stale in _projects.Where(p => p.Key != keep && p.Value.Snapshot is not null)
                     .OrderByDescending(p => p.Value.LastUsed)
                     .Skip(KeptProjects - 1)
                     .ToList())
            stale.Value.Snapshot = null;
    }
}
