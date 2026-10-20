using AiThread.Service.Orchestration;

namespace AiThread.Service.Interfaces;

public interface IExecutionEngine
{
    Task<ExecutionSummary> RunAsync(ExecutionRequest request, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
