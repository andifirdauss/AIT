using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    public async Task<ThreadResult> RunAsync(ThreadRequest request, IThreadObserver observer, CancellationToken cancellationToken)
    {
        var prepared = await PrepareOrResumeAsync(request, cancellationToken);
        return await ExecuteAsync(prepared, observer, cancellationToken);
    }
}
