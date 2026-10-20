using AiThread.Common.Entities;
using AiThread.Service.Memory;
using AiThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Orchestration;

public sealed partial class MemoryExtractor
{
    // Planned files, files the tasks reported, and files the prompt names. A failing code map only costs the hints.
    private async Task<IReadOnlyList<CodeMapHit>> MapFilesAsync(ProjectContext? project, ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution,
        CancellationToken cancellationToken)
    {
        if (project is null || string.IsNullOrEmpty(project.Key)) return [];
        try
        {
            var files = plan.Tasks.Select(t => t.File)
                .Concat(plan.Tasks.Select(t => t.To).OfType<string>())
                .Concat(execution?.Results.Select(r => r.File) ?? [])
                .Concat(await codeMapper.ResolveReferencesAsync(project, thread.Prompt, cancellationToken))
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(60)
                .ToList();
            return await codeMapper.MapAsync(project, files, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Code map for the memory extraction of thread {ThreadId} failed.", thread.Id);
            return [];
        }
    }
}
