namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    private void PruneExpiredSessions(DateTime now)
    {
        if (_sessions.Count < 500) return;
        foreach (var (key, decision) in _sessions)
            if (decision.ExpiresAt <= now) _sessions.TryRemove(key, out _);
    }
}
