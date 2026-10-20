using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Runs the unfinished tasks of a finished thread again from its stored plan (no planner call), then verifies and
    // wraps like a normal run. Tasks that succeeded are kept; earlier fix tasks that did not succeed are replaced by
    // the new verification.
    public async Task<ThreadResult> RetryAsync(Guid threadId, IThreadObserver observer, CancellationToken cancellationToken)
    {
        var prepared = await PrepareRetryAsync(threadId, cancellationToken, cancellationToken);
        return await ExecuteRetryAsync(prepared, observer);
    }
}
