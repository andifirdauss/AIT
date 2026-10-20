using System.Diagnostics;
using AiThread.Common.Enums;
using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private async Task<ThreadResult> FailAsync(ThreadRecorder recorder, ClientProgress progress, Stopwatch total, string message)
    {
        await progress.FinalAsync($"❌ AIT gagal: {message}", CancellationToken.None);
        await FinishAsync(recorder, ThreadStatus.Failed, null, total, 0, null, message);
        recorder.Log(ActivityLevel.Error, $"Thread gagal: {message}");
        return new ThreadResult(recorder.Thread.Id, ThreadStatus.Failed, progress.Transcript, TotalTokens(recorder.Thread));
    }
}
