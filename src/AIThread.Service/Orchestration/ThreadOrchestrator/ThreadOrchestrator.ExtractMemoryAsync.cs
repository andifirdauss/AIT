using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The memory extraction runs on the application's token, so the memories are kept even when the client hangs up
    // while the answer waits. Null: it did not finish within MemoryWaitSeconds (or the client left); it goes on anyway.
    private async Task<MemoryExtraction?> ExtractMemoryAsync(
        AiNode node, ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution, string finalText, CancellationToken cancellationToken)
    {
        var extraction = Task.Run(async () =>
        {
            var result = await memoryExtractor.ExtractAsync(node, thread, plan, execution, finalText, lifetime.ApplicationStopping);
            await NotifyMemoryAsync(result, thread.Id);
            return result;
        }, CancellationToken.None);
        try
        {
            return await extraction.WaitAsync(TimeSpan.FromSeconds(Math.Max(0, options.Value.MemoryWaitSeconds)), cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
