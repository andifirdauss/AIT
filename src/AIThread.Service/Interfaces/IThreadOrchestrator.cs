using AIThread.Service.Models;
using AIThread.Service.Testing;

namespace AIThread.Service.Interfaces;

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

    // Runs the Auto Testing of a completed thread again in the background (e.g. after the app was started), from its
    // stored test plan, with login when given (not stored). Throws AppValidationException when the thread has nothing
    // to test. Returns the thread id.
    Task<Guid> RetestInBackgroundAsync(Guid threadId, TestLogin? login, CancellationToken cancellationToken);

    // Runs one of the tester's suggested follow-up tests of a completed thread in the background. Throws
    // AppValidationException when the thread or the suggestion cannot run. Returns the thread id.
    Task<Guid> FollowUpInBackgroundAsync(Guid threadId, int number, CancellationToken cancellationToken);
}
