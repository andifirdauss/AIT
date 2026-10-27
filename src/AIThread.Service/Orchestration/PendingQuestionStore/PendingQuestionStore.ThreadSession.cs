namespace AIThread.Service.Orchestration;

public sealed partial class PendingQuestionStore
{
    // The key of a thread without a session (dashboard): its question is answered on the thread page.
    public static string ThreadSession(Guid threadId) => $"thread:{threadId:N}";
}
