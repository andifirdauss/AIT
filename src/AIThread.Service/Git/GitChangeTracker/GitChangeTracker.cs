using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using AIThread.Common.Enums;
using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

// Git when a thread started: branch ("HEAD" when detached), commit (null before the first commit) and the developer's
// git identity "Name <email>".
public sealed record GitOrigin(string? Branch, string? Commit, string? Author);

// Work for GitWatchWorker: ThreadId = a thread finished changing files; otherwise git of the project moved.
public sealed record GitTrackRequest(Guid ProjectId, Guid? ThreadId = null);

// Place, the state in the author's clone and the branch the change was made on.
public sealed record MemoryGitPresence(MemoryGitPlace Place, GitChangeState? State, string? Branch, string? Author)
{
    public static readonly MemoryGitPresence Unknown = new(MemoryGitPlace.Here, null, null, null);
}

// The last automatic or manual git fetch of a project.
public sealed record GitFetchStatus(DateTime At, string? Error);

// Follows the change of every AIT thread through git: whether it is still only in the working tree, staged, committed
// (and in which commit), pushed, stashed, changed again or thrown away outside AIT, left on another branch, or lost with
// a dropped commit. The fingerprint is git's blob id of what the thread wrote (ThreadFileSnapshot.AfterBlob), so a
// squash, rebase or cherry-pick of the same content is still recognised; a commit that holds the thread's lines with the
// developer's own edits on top is found by those lines. The state is kept on the thread and its file snapshots, and
// shared with the other developers through ThreadChanges next to long memory, where the planner of every clone reads
// whether a memory's change is in the branch it has checked out (PresenceAsync). git only reads, except the periodic
// fetch of the remotes.
public sealed partial class GitChangeTracker(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    LongMemoryStore longStore,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<GitChangeTracker> logger) : IGitChangeTracker
{
    public const int DefaultFetchMinutes = 15;
    public const int MaxFetchMinutes = 1440;
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromMinutes(2);

    // Stash entries looked into for a thrown-away change.
    private const int MaxStashes = 10;

    // Share of the thread's added lines a commit must hold (and of its removed lines it must lack) to count as the
    // thread's change with the developer's edits on top.
    private const double LineMatchShare = 0.8;

    // Answers of PresenceAsync per clone, HEAD and thread: HEAD moves rarely between two planner runs.
    private const int MaxPresenceCache = 5_000;
    private readonly ConcurrentDictionary<(string Root, string Head, Guid Thread), MemoryGitPresence> _presence = new();

    private readonly ConcurrentDictionary<Guid, GitFetchStatus> _fetches = new();

    // Threads followed after they finished; older ones keep the last state seen.
    public static readonly TimeSpan TrackedFor = TimeSpan.FromDays(30);
    public const int MaxThreadsPerRefresh = 200;

    // CurrentHash of a file that exists but cannot be read now: equal to no hash.
    private const string Unreadable = "?";

    private static readonly TimeSpan OriginTimeout = TimeSpan.FromSeconds(5);

    // Paths per git call, well within the command line limit of Windows.
    private const int PathsPerCall = 100;

    private readonly Channel<GitTrackRequest> _channel = Channel.CreateUnbounded<GitTrackRequest>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<GitTrackRequest> Reader => _channel.Reader;

    // What git says about one path: its blob in the index and in HEAD (null = not there), whether it is ignored.
    private sealed record PathFacts(string? IndexBlob, string? HeadBlob, bool Ignored)
    {
        public static readonly PathFacts None = new(null, null, false);
    }

    private static readonly JsonSerializerOptions FilesJson = new(JsonSerializerDefaults.Web);

    // One file of ThreadChange.Files.
    private sealed record SharedFile(string Path, string? Blob, bool Removed);

    // A file snapshot as the state is worked out from it.
    private sealed record TrackedFile(Guid Id, Guid ThreadId, string FilePath, bool Existed, bool Removed, string? OriginalHash, string? AfterHash,
        string? AfterBlob, GitChangeState? State, string? Commit, bool Edited);

    // The state worked out for one file.
    private sealed record FileResult(GitChangeState? State, string? Commit, bool Edited);

    // The lines a thread added to and removed from one file, from the diffs of its tasks.
    private sealed record ThreadLines(IReadOnlySet<string> Added, IReadOnlySet<string> Removed);

    // What git is asked once per refresh of a folder, whatever the number of threads.
    private sealed class RefreshCache(IReadOnlyList<string> paths)
    {
        // Every path of the refresh, relative to the folder.
        public IReadOnlyList<string> Paths => paths;

        public Dictionary<string, IReadOnlyList<string>> History { get; } = [];
        public Dictionary<string, string?> LastCommits { get; } = [];
        public Dictionary<string, bool> Pushed { get; } = [];
        public Dictionary<string, bool> Reachable { get; } = [];
        public Dictionary<string, bool> InHead { get; } = [];
        public Dictionary<string, IReadOnlyList<string>?> HeadLines { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<string>>? StashBlobs { get; set; }
    }
}
