using AiThread.Service.Models;

namespace AiThread.Service.Interfaces;

public interface IThreadOrchestrator
{
    // Runs planner -> parallel executors -> wrapper. Failures are reported in the result, not thrown.
    Task<ThreadResult> RunAsync(ThreadRequest request, IThreadObserver observer, CancellationToken cancellationToken);

    // Fire-and-forget variant for runs started from the dashboard. Returns the thread id once it exists.
    Task<Guid> StartInBackgroundAsync(ThreadRequest request, CancellationToken cancellationToken);

    // Runs the planned tasks of a finished thread that did not succeed again, from its stored plan (no planner call),
    // then verifies and wraps again. Throws AppValidationException when the thread cannot be retried.
    Task<ThreadResult> RetryAsync(Guid threadId, IThreadObserver observer, CancellationToken cancellationToken);

    // Validates like RetryAsync, then runs in the background. Returns the thread id.
    Task<Guid> RetryInBackgroundAsync(Guid threadId, CancellationToken cancellationToken);
}
