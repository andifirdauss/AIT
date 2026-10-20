using AIThread.Service.Orchestration;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ThreadService
{
    // A running thread is cancelled through its token; one waiting on a planner question just stops waiting.
    public bool Cancel(Guid id)
    {
        if (registry.Cancel(id)) return true;
        if (!questions.Remove(id)) return false;

        _ = Task.Run(async () =>
        {
            try
            {
                await UnansweredQuestions.CloseAsync(dbFactory, eventBus, [id], "Thread dibatalkan saat menunggu jawaban.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not close thread {ThreadId} that was waiting for an answer.", id);
            }
        });
        return true;
    }
}
