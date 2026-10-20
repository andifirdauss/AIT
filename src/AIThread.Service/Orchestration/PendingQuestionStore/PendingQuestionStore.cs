using System.Collections.Concurrent;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Orchestration;

// Threads paused on a planner question, by session: the next prompt of that session is the answer. Kept in memory
// only — after a restart the question is gone and the next prompt starts a new thread (ThreadLifecycleWorker closes
// the paused thread).
public sealed partial class PendingQuestionStore(TimeProvider timeProvider) : IPendingQuestionStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, PendingQuestion> _bySession = new(StringComparer.Ordinal);
}
