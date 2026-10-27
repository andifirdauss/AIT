using System.Collections.Concurrent;
using AIThread.Service.Git;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Approvals;

// A commit/push of a finished thread waiting for the developer's permission. SessionId is the chat it was asked in (or
// the thread's own key for a dashboard thread, PendingQuestionStore.ThreadSession).
public sealed record PendingPublish(Guid ThreadId, Guid ProjectId, string SessionId, ApprovalRequest Request, PublishJob Job, DateTime ExpiresAt);

// Commit/push requests of finished threads, by session: the next "1", "2" or "3" of that chat answers it, as does a
// button on the thread page. Kept in memory only; after a restart the developer asks again.
public sealed partial class PendingApprovalStore(TimeProvider timeProvider) : IPendingApprovalStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, PendingPublish> _bySession = new(StringComparer.Ordinal);
}
