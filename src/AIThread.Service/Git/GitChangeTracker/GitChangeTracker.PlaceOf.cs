using AIThread.Common.Enums;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The place of a thread of this clone from its own state.
    private static MemoryGitPlace PlaceOf(GitChangeState? state) => state switch
    {
        GitChangeState.OnOtherBranch => MemoryGitPlace.OtherBranch,
        GitChangeState.Stashed => MemoryGitPlace.InProgress,
        GitChangeState.Discarded or GitChangeState.Lost => MemoryGitPlace.Gone,
        _ => MemoryGitPlace.Here
    };
}
