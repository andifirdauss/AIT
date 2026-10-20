using AiThread.Service.Models;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    // decided: the number of these paths saved so far, after every batch.
    private async Task DecideAsync(ProjectContext project, IReadOnlyList<string> paths, Action<int>? decided, CancellationToken cancellationToken)
    {
        var areas = CodeMapHeuristics.AreaFolders(project);
        var groups = paths
            .GroupBy(p => CodeMapHeuristics.ModuleStem(p) ?? "(" + (Path.GetDirectoryName(p)?.Replace('\\', '/') ?? ".") + ")", StringComparer.OrdinalIgnoreCase)
            .Select((g, i) => new FileGroup($"G{i + 1}", g.Key, g.ToList()))
            .ToList();

        if (!await ai.IsAvailableAsync(cancellationToken))
        {
            await SaveFallbackAsync(project.Key, groups, areas, cancellationToken);
            decided?.Invoke(paths.Count);
            return;
        }

        // Model calls run side by side; their answers are saved one at a time, so a module created by one batch is
        // resolved (not created again) by the next. The first batch runs alone and seeds the module list the others see.
        var batches = groups.Chunk(GroupsPerCall).ToList();
        var done = 0;
        using var apply = new SemaphoreSlim(1, 1);
        async ValueTask RunAsync(FileGroup[] batch, CancellationToken token)
        {
            var answers = await AskAsync(project, batch, token);
            await apply.WaitAsync(token);
            try
            {
                done += await ApplyAsync(project.Key, batch, answers, areas, token);
                decided?.Invoke(done);
            }
            finally
            {
                apply.Release();
            }
        }

        await RunAsync(batches[0], cancellationToken);
        await Parallel.ForEachAsync(batches.Skip(1), new ParallelOptions { MaxDegreeOfParallelism = ParallelCalls, CancellationToken = cancellationToken }, RunAsync);
    }
}
