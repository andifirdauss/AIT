using System.Threading.Channels;
using AIThread.Common.DTOs;
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

    Task<GitFetchStatus?> FetchAsync(Guid projectId, CancellationToken cancellationToken = default);

    GitFetchStatus? LastFetch(Guid projectId);

    Task<ProjectGitDto?> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectGitDto?> SetFetchMinutesAsync(Guid projectId, int? minutes, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, MemoryGitPresence>> PresenceAsync(Guid projectId, IReadOnlyCollection<Guid> threadIds,
        CancellationToken cancellationToken = default);
}
