using AiThread.Service.Orchestration;

namespace AiThread.Service.Interfaces;

public interface IVerificationStage
{
    Task<VerificationResult> RunAsync(VerificationInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
