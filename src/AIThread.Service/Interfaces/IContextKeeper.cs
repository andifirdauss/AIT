using AIThread.Common.Entities;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Proxy;

namespace AIThread.Service.Interfaces;

public interface IContextKeeper
{
    Task<ResolvedSession?> ResolveAsync(Guid projectId, IReadOnlyList<ThreadAnchorRef> anchors, CancellationToken cancellationToken);

    Task<ResolvedChat?> ResolveChatAsync(Guid projectId, string sessionId, CancellationToken cancellationToken);

    Task<string> ReadThreadAsync(Guid projectId, string id, CancellationToken cancellationToken);

    Task UpdateAsync(ThreadRun thread, ThreadRequest request, CancellationToken cancellationToken);

    Task<(string Pack, string Author)> WritePackAsync(string input, Func<string> fallback, Guid? threadId, Guid? projectId, CancellationToken cancellationToken);
}
