namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    // F2b + F3: the registered projects with waiting memory, most first: the files their memory names are mapped, then
    // that memory is relabeled. Then every memory still waiting (knowledge, keys of projects registered elsewhere,
    // project memory known only by project id).
    private async Task<(int Labeled, int Reviewed)> MigrateAsync(IReadOnlyDictionary<Guid, (string? Key, string Name)> projects,
        IReadOnlyList<RegisteredProject> registered, IReadOnlyDictionary<string, List<string>> pendingByKey, ISet<string> relabeledKeys,
        CancellationToken cancellationToken)
    {
        _batchesLeft = MaxBatchesPerRun;
        var withMemory = registered.Where(p => pendingByKey.ContainsKey(p.Key)).OrderByDescending(p => pendingByKey[p.Key].Count).ToList();
        int labeled = 0, reviewed = 0;
        for (var i = 0; i < withMemory.Count; i++)
        {
            var project = withMemory[i];
            var label = $"{project.Name} ({i + 1}/{withMemory.Count})";
            await MapReferencedAsync(project, label, pendingByKey[project.Key], cancellationToken);

            SetPhase("Relabel", label);
            var (l, r) = await RelabelAsync(projects, project.Key, relabeledKeys, cancellationToken);
            labeled += l;
            reviewed += r;
        }

        SetPhase("Relabel", withMemory.Count > 0 ? "memory lainnya" : null);
        var (restLabeled, restReviewed) = await RelabelAsync(projects, null, relabeledKeys, cancellationToken);
        return (labeled + restLabeled, reviewed + restReviewed);
    }
}
