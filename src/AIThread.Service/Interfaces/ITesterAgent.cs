using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface ITesterAgent
{
    Task<TestingResult> RunAsync(TestingInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);

    // Why unit tests fail after a change: one verdict per failure, with the file to fix. Never throws for a node that
    // cannot answer: the result then carries the error.
    Task<UnitTestTriageResult> TriageAsync(UnitTestTriageInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
