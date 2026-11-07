using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

// Commits made outside AIT, summarized into the project's change log (memory) and, through it, into the business wiki
// and its environments. Opt-in per project key (CommitIngestSettings, shared): it costs tokens of the cheap memory node.
// Each pass reads the commits of the chosen ref since the moment it was turned on, oldest first, and leaves out those
// that are AIT's own (a commit or squash commit of a shared thread change, or a commit whose every file holds what an AIT
// thread wrote). A commit is claimed in the shared store first (IngestedCommit), so one developer's AIT takes it; the
// summary becomes a Change memory whose source is the commit, and a ThreadChange row (Ingested) lets git state,
// environments and the wiki treat it like a thread.
// The branch checked out in the clone is read too (WorkingBranchAsync: its commits not in the ref yet), so a change made
// with another tool is in memory before it is merged. Such a commit can come back with another id: rebased or
// cherry-picked (same git patch-id) or squash merged (every file holds a blob a summarized commit of another branch
// wrote). It is then recorded as a copy (CopyOf): its id joins the summarized commit's ThreadChange instead of a second
// summary, so the memory stays when the branch is deleted. The AIT that took a working-branch commit on follows it in its
// clone (RefreshBranchesAsync): pushed, still on a branch, or dropped (Lost, the planner leaves its memory out).
public sealed partial class CommitIngestor(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    LongMemoryStore longStore,
    IProjectService projectService,
    IMemoryAi ai,
    IMemoryService memoryService,
    ICodeMapper codeMapper,
    IModuleRegistry registry,
    ISecretGuard secrets,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<CommitIngestor> logger) : ICommitIngestor
{
    private const int MaxDiffChars = 14_000;
    private const int MaxMessageChars = 2_000;
    private const int MaxScannedCommits = 2_000;
    private const int MaxMappedFiles = 40;
    private const int MaxTokens = 3_000;
    private const int RecentShown = 20;

    // The window of commits counted for the suggestion to turn it on, and the most counted.
    public const int SuggestDays = 30;
    private const int MaxSuggested = 200;

    // A patch larger than this gets no patch-id (a rebase of it is then summarized again).
    private const int MaxPatchChars = 4_000_000;

    // Working-branch commits followed after they were taken on.
    private static readonly TimeSpan FollowedFor = TimeSpan.FromDays(30);

    // One pass per project at a time on this machine.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // What git says about one commit.
    private sealed record CommitInfo(string Sha, string Author, DateTime CommittedAt, string Message, IReadOnlyList<SharedFile> Files);

    // A commit to look at, and the working branch it comes from (null = the ingest ref).
    private sealed record Candidate(string Sha, string? Branch);

    // The branch checked out here and its commits not in the ingest ref, oldest first.
    private sealed record WorkingBranch(string Name, IReadOnlyList<string> Commits);

    // What the commits summarized before left: their patch-ids, every path + blob a working-branch one wrote, and the
    // paths each working-branch one touched.
    private sealed class Summarized
    {
        public Dictionary<string, (Guid Id, string Commit)> Patches { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, (Guid Id, string? Branch)> Blobs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(Guid Id, string Branch, IReadOnlySet<string> Paths)> BranchCommits { get; } = [];

        public void AddBranchCommit(Guid id, string branch, IReadOnlyList<SharedFile> files)
        {
            foreach (var file in files.Where(f => !f.Removed && f.Blob is not null)) Blobs.TryAdd($"{file.Path}|{file.Blob}", (id, branch));
            BranchCommits.Add((id, branch, files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase)));
        }
    }

    // A commit that is a copy of summarized ones: the ThreadChanges (= ingest ids) it joins, and why.
    private sealed record IngestCopy(IReadOnlyList<Guid> ChangeIds, string Note);
}
