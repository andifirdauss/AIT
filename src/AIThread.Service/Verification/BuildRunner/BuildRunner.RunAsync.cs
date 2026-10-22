using Microsoft.Extensions.Logging;

namespace AIThread.Service.Verification;

public sealed partial class BuildRunner
{
    public async Task<BuildResult> RunAsync(BuildCommand command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var gate = RootGates.For(command.Root);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (command.Incremental is { } incremental && FirstInUse(command.OutputDirectories) is { } inUse)
            {
                logger.LogInformation("Build {Command} runs without clean: {File} is in use", command.Display, inUse);
                return await RunCoreAsync(incremental, timeout, cancellationToken) with { InUseOutput = inUse };
            }
            return await RunCoreAsync(command, timeout, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
