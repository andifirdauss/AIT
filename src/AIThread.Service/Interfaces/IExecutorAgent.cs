using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface IExecutorAgent
{
    Task<ExecutorOutcome> ExecuteAsync(ExecutorJob job, CancellationToken cancellationToken);
}
