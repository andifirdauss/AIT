using AiThread.Service.Verification;

namespace AiThread.Service.Interfaces;

public interface IBuildRunner
{
    Task<BuildResult> RunAsync(BuildCommand command, TimeSpan timeout, CancellationToken cancellationToken);
}
