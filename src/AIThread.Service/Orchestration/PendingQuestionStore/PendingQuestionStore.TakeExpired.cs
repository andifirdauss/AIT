namespace AIThread.Service.Orchestration;

public sealed partial class PendingQuestionStore
{
    public IReadOnlyList<PendingQuestion> TakeExpired()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expired = new List<PendingQuestion>();
        foreach (var (session, pending) in _bySession)
            if (pending.ExpiresAt <= now && _bySession.TryRemove(new KeyValuePair<string, PendingQuestion>(session, pending)))
                expired.Add(pending);
        return expired;
    }
}
