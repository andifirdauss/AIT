using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface IExecutionEngine
{
    Task<ExecutionSummary> RunAsync(ExecutionRequest request, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
