using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The memory extraction runs on the application's token, so the memories are kept even when the client hangs up
    // while the answer waits. Pending: it did not finish within MemoryWait (or the client left); it goes on anyway.
    private async Task<(MemoryExtraction? Result, bool Pending)> ExtractMemoryAsync(
        AiNode node, ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution, string finalText, CancellationToken cancellationToken)
    {
        var extraction = Task.Run(() => memoryExtractor.ExtractAsync(node, thread, plan, execution, finalText, lifetime.ApplicationStopping), CancellationToken.None);
        try
        {
            return (await extraction.WaitAsync(MemoryWait, cancellationToken), false);
        }
        catch (TimeoutException)
        {
            return (null, true);
        }
        catch (OperationCanceledException)
        {
            return (null, true);
        }
    }
}
