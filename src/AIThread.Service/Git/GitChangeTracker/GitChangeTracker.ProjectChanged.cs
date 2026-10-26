namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Git of the project moved (commit, checkout, reset, stash, ...): the threads it follows are looked at again.
    public void ProjectChanged(Guid projectId) => _channel.Writer.TryWrite(new GitTrackRequest(projectId));
}
