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
// IsAuto: not registered, a remote branch of this clone taken as an environment by itself (Id empty, not saved).
public record ProjectEnvironmentDto(
    Guid Id,
    string Name,
    string Ref,
    int Order,
    bool IsProduction,
    bool RefFound,
    string? RefCommit,
    int Changes,
    DateTime? LastSeenAt,
    bool IsAuto = false);

public record SaveProjectEnvironment(string Name, string Ref, bool IsProduction = false);

// The whole list, in display order: environments left out are removed.
public record SaveProjectEnvironmentsRequest(IReadOnlyList<SaveProjectEnvironment> Environments);

// Summarizing commits made outside AIT (shared per project key). Ref = as set (null = the production environment's
// ref, else origin/HEAD); EffectiveRef = what is read in this clone (null: none found). Counts per status.
// WorkingBranch: the branch checked out here is read too (its commits not in the ref yet); CurrentBranch = that branch
// (null when HEAD is detached). Outside: while it is off, the commits made outside AIT in the last OutsideDays days that
// it would take on (the ref's and the working branch's), so the card can suggest turning it on; null when on or unknown.
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
    IReadOnlyList<IngestedCommitDto> Recent,
    bool WorkingBranch = true,
    string? CurrentBranch = null,
    int? Outside = null,
    int OutsideDays = 0);

// Branch: the working branch it was read from (null = the ref).
public record IngestedCommitDto(Guid Id, string Commit, IngestStatus Status, string? Title, string? Author, DateTime? CommittedAt, string? ClaimedBy,
    Guid? MemoryId, string? Note, DateTime CreatedAt, string? Branch = null);

// Since: null keeps the stored moment (turning it on for the first time starts now). WorkingBranch: null keeps it.
public record SaveCommitIngestRequest(bool Enabled, string? Ref, DateTime? Since, int? MaxPerRun, bool? WorkingBranch = null);

public record CommitIngestResultDto(int Ingested, int Skipped, int Failed, string? Problem);
