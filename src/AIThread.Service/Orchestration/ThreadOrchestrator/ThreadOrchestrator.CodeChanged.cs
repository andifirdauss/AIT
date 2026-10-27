namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The thread's tasks are done (or stopped): Project > Detail → Struktur kode is walked again, the functions the
    // thread changed get their calls read by the tools node, and git's view of the change is followed from now on. All
    // in the background.
    private void CodeChanged(Guid projectId, Guid threadId)
    {
        codeStructure.Refresh(projectId);
        functionCalls.ThreadChanged(projectId, threadId);
        gitChanges.ThreadChanged(projectId, threadId);
    }
}
