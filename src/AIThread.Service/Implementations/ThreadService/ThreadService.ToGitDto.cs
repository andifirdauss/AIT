using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class ThreadService
{
    // Null when git knew nothing of the thread (not a repository, or a thread from before git was followed).
    private static ThreadGitDto? ToGitDto(ThreadRun thread, IReadOnlyList<ThreadGitFileDto> files)
    {
        if (thread.GitBranch is null && thread.GitBaseCommit is null && thread.GitAuthor is null && thread.GitCheckedAt is null) return null;
        var commits = (thread.GitCommits ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new ThreadGitDto(thread.GitBranch, thread.GitBaseCommit, thread.GitAuthor, thread.GitState, commits, thread.GitCheckedAt,
            thread.GitCheckedAt is null ? [] : files);
    }
}
