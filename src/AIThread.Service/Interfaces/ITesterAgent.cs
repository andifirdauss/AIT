using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface ITesterAgent
{
    Task<TestingResult> RunAsync(TestingInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
