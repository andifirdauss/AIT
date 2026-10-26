using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Runs one of the tester's suggestions from the thread page; progress arrives over SSE.
    public async Task<Guid> FollowUpInBackgroundAsync(Guid threadId, int number, CancellationToken cancellationToken)
    {
        var prepared = await PrepareRetestAsync(threadId, lifetime.ApplicationStopping, cancellationToken);
        if (TestFollowUpReply.Find(prepared.Thread.TestingJson, number) is null)
        {
            registry.Complete(threadId);
            throw new AppValidationException(Lang.T($"Test lanjutan {number} tidak ada di thread ini.", $"Follow-up test {number} is not in this thread."));
        }
        _ = Task.Run(() => RunFollowUpAsync(prepared, number, new ClientProgress(NullThreadObserver.Instance, showProgress: false), canAskLogin: false),
            CancellationToken.None);
        return threadId;
    }
}
