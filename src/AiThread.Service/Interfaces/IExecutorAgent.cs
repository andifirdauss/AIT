using AiThread.Service.Orchestration;

namespace AiThread.Service.Interfaces;

public interface IExecutorAgent
{
    Task<ExecutorOutcome> ExecuteAsync(ExecutorJob job, CancellationToken cancellationToken);
}
