using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

// One file of a thread's change as git sees it now; Commit is set when it is committed (Edited: with the developer's own
// edits on top of the thread's lines).
public record ThreadGitFileDto(string FilePath, GitChangeState? State, string? Commit, bool Edited = false);

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

// Git of a project (Project > Detail): the automatic fetch (FetchMinutes as set, null = the default EffectiveFetchMinutes;
// 0 = never), the last fetch, and the threads whose change is followed.
public record ProjectGitDto(
    bool IsRepository,
    bool HasRemote,
    int? FetchMinutes,
    int EffectiveFetchMinutes,
    DateTime? LastFetchAt,
    string? LastFetchError,
    int FollowedThreads);

public record SetGitFetchRequest(int? FetchMinutes);

// An environment of the project (shared by the project key): RefFound/RefCommit = whether this clone knows the ref (and
// where it points) after its last fetch; Changes = AIT changes seen in it; LastSeenAt = when the newest one was seen.
public record ProjectEnvironmentDto(
    Guid Id,
    string Name,
    string Ref,
    int Order,
    bool IsProduction,
    bool RefFound,
    string? RefCommit,
    int Changes,
    DateTime? LastSeenAt);

public record SaveProjectEnvironment(string Name, string Ref, bool IsProduction = false);

// The whole list, in display order: environments left out are removed.
public record SaveProjectEnvironmentsRequest(IReadOnlyList<SaveProjectEnvironment> Environments);
