using AIThread.Service.Verification;

namespace AIThread.Service.Interfaces;

public interface IBuildRunner
{
    Task<BuildResult> RunAsync(BuildCommand command, TimeSpan timeout, CancellationToken cancellationToken);
}
