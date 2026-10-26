using System.Threading.Channels;
using AIThread.Common.Enums;
using AIThread.Data.Context;
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

// Follows the change of every AIT thread through git: whether it is still only in the working tree, staged, committed
// (and in which commit), changed again or thrown away outside AIT, or left on another branch. The fingerprint is git's blob id of what the thread
// wrote (ThreadFileSnapshot.AfterBlob), so a squash, rebase or cherry-pick of the same content is still recognised.
// Only git commands that read are run; the state is kept on the thread and its file snapshots.
public sealed partial class GitChangeTracker(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IEventBus eventBus,
    ILogger<GitChangeTracker> logger) : IGitChangeTracker
{
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

    // A file snapshot as the state is worked out from it.
    private sealed record TrackedFile(Guid Id, Guid ThreadId, string FilePath, bool Existed, bool Removed, string? OriginalHash, string? AfterHash,
        string? AfterBlob, GitChangeState? State, string? Commit);
}
