using System.Diagnostics;
using AiThread.Common.Enums;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private static Task FinishAsync(ThreadRecorder recorder, ThreadStatus status, string? finalText, Stopwatch total, int wrapTokens, long? wrappingMs, string? error = null) =>
        recorder.UpdateThreadAsync(t =>
        {
            t.Status = status;
            t.FinalResponse = finalText;
            t.WrapperTokens += wrapTokens;
            t.WrappingMs = wrappingMs;
            t.Error = error;
            // A thread resumed after a planner question already holds the time spent before it.
            t.TotalMs = (t.TotalMs ?? 0) + total.ElapsedMilliseconds;
            t.CompletedAt = DateTime.UtcNow;
        });
}
