using AIThread.Service.Git;

namespace AIThread.Service.Interfaces;

// Commit and push of a thread's files, with a new branch and a pull request when asked, after the developer allowed it
// (ApprovalKind.Branch / PullRequest / Commit / Push).
public interface IGitPublisher
{
    // files: full paths. Job is null with Error when nothing can be committed or pushed.
    Task<(PublishJob? Job, string? Error)> PrepareAsync(IReadOnlyList<string> files, bool commit, bool push, string? message, CancellationToken cancellationToken = default);

    Task<(PublishJob? Job, string? Error)> PrepareAsync(IReadOnlyList<string> files, PublishRequest request, CancellationToken cancellationToken = default);

    Task<PublishOutcome> RunAsync(PublishJob job, CancellationToken cancellationToken = default);
}
