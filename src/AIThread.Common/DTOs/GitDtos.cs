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

// Summarizing commits made outside AIT (shared per project key). Ref = as set (null = the production environment's
// ref, else origin/HEAD); EffectiveRef = what is read in this clone (null: none found). Counts per status.
public record CommitIngestDto(
    bool Enabled,
    string? Ref,
    string? EffectiveRef,
    DateTime Since,
    int MaxPerRun,
    int Done,
    int Skipped,
    int Failed,
    DateTime? LastIngestedAt,
    IReadOnlyList<IngestedCommitDto> Recent);

public record IngestedCommitDto(Guid Id, string Commit, IngestStatus Status, string? Title, string? Author, DateTime? CommittedAt, string? ClaimedBy,
    Guid? MemoryId, string? Note, DateTime CreatedAt);

// Since: null keeps the stored moment (turning it on for the first time starts now).
public record SaveCommitIngestRequest(bool Enabled, string? Ref, DateTime? Since, int? MaxPerRun);

public record CommitIngestResultDto(int Ingested, int Skipped, int Failed, string? Problem);
