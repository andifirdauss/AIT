using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

// One file of a thread's change as git sees it now; Commit is set when it is committed.
public record ThreadGitFileDto(string FilePath, GitChangeState? State, string? Commit);

// Git of a thread: where it started (branch, commit, the developer's git identity) and where its change is now.
// State and Files are empty until GitChangeTracker has looked (the thread changed no file, or it is not a repository).
public record ThreadGitDto(
    string? Branch,
    string? BaseCommit,
    string? Author,
    GitChangeState? State,
    IReadOnlyList<string> Commits,
    DateTime? CheckedAt,
    IReadOnlyList<ThreadGitFileDto> Files);
