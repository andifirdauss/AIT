namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The thread finished writing files: its fingerprint is taken and its state worked out, in the background.
    public void ThreadChanged(Guid projectId, Guid threadId) => _channel.Writer.TryWrite(new GitTrackRequest(projectId, threadId));
}
