using System.Threading.Channels;
using AIThread.Service.Git;

namespace AIThread.Service.Interfaces;

public interface IGitChangeTracker
{
    ChannelReader<GitTrackRequest> Reader { get; }

    Task<GitOrigin?> ReadOriginAsync(string root, CancellationToken cancellationToken = default);

    void ThreadChanged(Guid projectId, Guid threadId);

    void ProjectChanged(Guid projectId);

    Task<int> RefreshThreadsAsync(IReadOnlyCollection<Guid> threadIds, CancellationToken cancellationToken = default);

    Task<int> RefreshProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
}
