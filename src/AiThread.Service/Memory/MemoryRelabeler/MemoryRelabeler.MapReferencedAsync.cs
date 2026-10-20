using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    // Before relabeling a project: only the files its waiting memory names, which is all LookupAsync reads. Progress
    // goes to the status detail, e.g. "KN2018_KCO (1/5) · 12/40 file disebut memory".
    private async Task MapReferencedAsync(RegisteredProject project, string label, IReadOnlyList<string> descriptions, CancellationToken cancellationToken)
    {
        SetPhase("Peta kode", label);
        try
        {
            var context = await projectService.GetContextAsync(project.Id, cancellationToken);
            if (context is null) return;
            await codeMapper.MapReferencedAsync(context, descriptions, cancellationToken, (done, total) => ReportFiles(label, done, total, " disebut memory"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Code map of the files named by {Project}'s memory failed.", project.Name);
        }
    }
}
