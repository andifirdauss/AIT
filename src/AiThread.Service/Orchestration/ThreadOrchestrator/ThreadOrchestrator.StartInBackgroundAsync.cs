using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    public async Task<Guid> StartInBackgroundAsync(ThreadRequest request, CancellationToken cancellationToken)
    {
        var prepared = await PrepareOrResumeAsync(request, cancellationToken);
        _ = Task.Run(() => ExecuteAsync(prepared, NullThreadObserver.Instance, lifetime.ApplicationStopping), CancellationToken.None);
        return prepared.Thread.Id;
    }
}
