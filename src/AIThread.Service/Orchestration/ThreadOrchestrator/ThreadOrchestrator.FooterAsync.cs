using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Last part of the answer: time, tokens (memory extraction included), memory, wiki queue, the remaining quota of
    // the providers behind the nodes, and the thread page link.
    private async Task FooterAsync(
        ClientProgress progress,
        ThreadRequest request,
        RuntimeSettings settings,
        ThreadRun thread,
        ThreadStatus status,
        Stopwatch total,
        int memoriesUsed,
        MemoryExtraction? memory,
        bool memoryPending,
        CancellationToken cancellationToken)
    {
        // The thread is already finished; a client that left gets nothing more, and the thread stays as it is.
        if (cancellationToken.IsCancellationRequested) return;
        var wikiHours = settings.BusinessMemoryEnabled && status == ThreadStatus.Completed ? Math.Max(1, settings.BusinessMemoryIntervalHours) : (int?)null;
        var quotas = await NodeQuotasAsync(cancellationToken);
        var masked = secrets.TallyCount;
        if (masked > 0)
            activityLog.Info("keamanan", Lang.T($"{masked} secret disamarkan sebelum dikirim ke model.", $"{masked} secret(s) masked before being sent to a model."), thread.Id, thread.ProjectId);
        var footer = FinalReport.Footer(total.Elapsed, TotalTokens(thread) + (memory?.Tokens ?? 0), memoriesUsed, memory, memoryPending, wikiHours,
            request.DashboardUrl, thread.Id, quotas, secretsMasked: masked);
        await progress.FinalAsync(footer, cancellationToken);
    }
}
