namespace AiThread.Service.Orchestration;

public sealed partial class PendingQuestionStore
{
    public bool Remove(Guid threadId)
    {
        foreach (var (session, pending) in _bySession)
            if (pending.ThreadId == threadId && _bySession.TryRemove(new KeyValuePair<string, PendingQuestion>(session, pending)))
                return true;
        return false;
    }
}
