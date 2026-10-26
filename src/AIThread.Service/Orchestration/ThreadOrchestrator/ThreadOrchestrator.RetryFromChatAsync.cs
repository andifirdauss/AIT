using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // "ulangi" in the chat: the last thread's unfinished tasks run again like "🔁 Ulangi yang gagal", streamed back as
    // the reply. Esc in the chat cancels it like any thread. When the thread cannot be retried after all (it started
    // again meanwhile), the reason is the answer.
    private async Task<ThreadResult> RetryFromChatAsync(Guid threadId, ThreadRequest request, IThreadObserver observer, CancellationToken cancellationToken)
    {
        PreparedRetry prepared;
        try
        {
            prepared = await PrepareRetryAsync(threadId, cancellationToken, cancellationToken);
        }
        catch (AppValidationException ex)
        {
            var settings = await settingService.GetRuntimeAsync(cancellationToken);
            var progress = new ClientProgress(observer, settings.StreamProgressToClient);
            await progress.FinalAsync(Lang.T($"🔁 Tugas yang gagal tidak bisa diulang: {ex.Message}", $"🔁 The failed tasks cannot be retried: {ex.Message}"), cancellationToken);
            return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, 0);
        }

        activityLog.Info("thread", Lang.T("Percobaan ulang diminta dari chat.", "Retry requested from the chat."), threadId, prepared.Project.ProjectId);
        return await ExecuteRetryAsync(prepared, observer, request);
    }
}
