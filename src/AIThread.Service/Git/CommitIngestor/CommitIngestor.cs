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

    // One pass per project at a time on this machine.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // What git says about one commit.
    private sealed record CommitInfo(string Sha, string Author, DateTime CommittedAt, string Message, IReadOnlyList<SharedFile> Files);
}
