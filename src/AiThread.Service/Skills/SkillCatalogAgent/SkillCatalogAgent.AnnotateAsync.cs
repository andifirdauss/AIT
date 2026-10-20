using AiThread.Service.Models;
using AiThread.Service.Orchestration;

namespace AiThread.Service.Skills;

public sealed partial class SkillCatalogAgent
{
    // Same order as skills; skills the model dropped are removed, the others get tags (and a description when missing).
    // Large repositories (hundreds of SKILL.md) take many batches, so a few run at once; onProgress gets (done, total).
    // A batch the model fails on keeps its skills untagged instead of failing the whole pull.
    public async Task<IReadOnlyList<DiscoveredSkill>> AnnotateAsync(
        string sourceName, IReadOnlyList<DiscoveredSkill> skills, Func<int, int, Task>? onProgress, CancellationToken cancellationToken)
    {
        var batches = skills.Chunk(AnnotateBatch).ToList();
        var results = new IReadOnlyList<DiscoveredSkill>[batches.Count];
        var done = 0;
        var failed = 0;
        using var gate = new SemaphoreSlim(ParallelBatches);

        async Task RunAsync(int index)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var batch = batches[index];
                try
                {
                    var (json, _, _) = await ai.AskJsonAsync(AgentPrompts.SkillAnnotate, AnnotateInput(sourceName, batch), 6_000, cancellationToken);
                    results[index] = ApplyAnnotations(batch, json);
                }
                catch (NineRouterException)
                {
                    Interlocked.Increment(ref failed);
                    results[index] = batch;
                }
                if (onProgress is not null) await onProgress(Interlocked.Increment(ref done), batches.Count);
            }
            finally
            {
                gate.Release();
            }
        }

        await Task.WhenAll(Enumerable.Range(0, batches.Count).Select(RunAsync));
        // Nothing got through: let the caller report the orchestrator as unreachable.
        if (failed == batches.Count && batches.Count > 0)
            throw new NineRouterException("Skill Orchestrator gagal pada semua batch.");
        return results.SelectMany(r => r).ToList();
    }
}
