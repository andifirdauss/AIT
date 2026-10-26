using AIThread.Common.Entities;
using AIThread.Service.Memory;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class MemoryExtractor
{
    // Changed files first, then the planned ones and the files the prompt names. A failing code map only costs the hints.
    private async Task<IReadOnlyList<CodeMapHit>> MapFilesAsync(ProjectContext? project, ThreadRun thread, ThreadPlan plan, IReadOnlyList<ChangeLogFile> changed,
        CancellationToken cancellationToken)
    {
        if (project is null || string.IsNullOrEmpty(project.Key)) return [];
        try
        {
            var files = changed.Select(f => f.Path)
                .Concat(plan.Tasks.Select(t => t.File))
                .Concat(plan.Tasks.Select(t => t.To).OfType<string>())
                .Concat(await codeMapper.ResolveReferencesAsync(project, thread.Prompt, cancellationToken))
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxMappedFiles)
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
