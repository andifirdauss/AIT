using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // A prompt from a session whose thread is waiting on a planner question is the answer: that thread continues.
    private async Task<PreparedThread> PrepareOrResumeAsync(ThreadRequest request, CancellationToken cancellationToken)
    {
        var pending = questions.TryTake(request.SessionId, request.ProjectId, out var stale);
        if (stale is not null)
            await UnansweredQuestions.CloseAsync(dbFactory, eventBus, [stale.ThreadId], Lang.T("Pertanyaan planner tidak dijawab; prompt berikutnya dijalankan sebagai thread baru.", "The planner's question was not answered; the next prompt runs as a new thread."));

        if (pending is not null && await ResumeAsync(pending, request, cancellationToken) is { } resumed) return resumed;
        return await PrepareAsync(request, cancellationToken);
    }
}
