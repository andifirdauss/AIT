using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface IVerificationStage
{
    Task<VerificationResult> RunAsync(VerificationInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
