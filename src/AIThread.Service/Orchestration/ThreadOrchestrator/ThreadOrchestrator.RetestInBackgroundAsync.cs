using AIThread.Service.Models;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Runs the test of a finished thread again, e.g. once the developer has started the app: the stored plan's test
    // section against the changes that succeeded, with the login typed on the thread page (not stored). Code,
    // verification and the final answer stay as they are; the thread page follows the run over SSE.
    public async Task<Guid> RetestInBackgroundAsync(Guid threadId, TestLogin? login, CancellationToken cancellationToken)
    {
        var prepared = await PrepareRetestAsync(threadId, lifetime.ApplicationStopping, cancellationToken);
        _ = Task.Run(() => RetestAsync(prepared, login, new ClientProgress(NullThreadObserver.Instance, showProgress: false), canAskLogin: false),
            CancellationToken.None);
        return threadId;
    }
}
