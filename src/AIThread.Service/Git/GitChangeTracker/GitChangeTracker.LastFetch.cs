namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The last fetch of the project since AIT started; null when none ran yet.
    public GitFetchStatus? LastFetch(Guid projectId) => _fetches.TryGetValue(projectId, out var status) ? status : null;
}
