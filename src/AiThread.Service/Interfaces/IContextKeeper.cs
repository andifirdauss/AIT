using AiThread.Common.Entities;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Proxy;

namespace AiThread.Service.Interfaces;

public interface IContextKeeper
{
    Task<ResolvedSession?> ResolveAsync(Guid projectId, IReadOnlyList<ThreadAnchorRef> anchors, CancellationToken cancellationToken);

    Task<string> ReadThreadAsync(Guid projectId, string id, CancellationToken cancellationToken);

    Task UpdateAsync(ThreadRun thread, ThreadRequest request, CancellationToken cancellationToken);

    Task<(string Pack, string Author)> WritePackAsync(string input, Func<string> fallback, Guid? threadId, Guid? projectId, CancellationToken cancellationToken);
}
