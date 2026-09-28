using AiThread.Service.Models;

namespace AiThread.Service.Interfaces;

public interface IThreadOrchestrator
{
    // Runs planner -> parallel executors -> wrapper. Failures are reported in the result, not thrown.
    Task<ThreadResult> RunAsync(ThreadRequest request, IThreadObserver observer, CancellationToken cancellationToken);

    // Fire-and-forget variant for runs started from the dashboard. Returns the thread id once it exists.
    Task<Guid> StartInBackgroundAsync(ThreadRequest request, CancellationToken cancellationToken);
}
