namespace AIThread.Service.Verification;

public sealed partial class BuildRunner
{
    public async Task<BuildResult> RunAsync(BuildCommand command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var gate = RootGates.For(command.Root);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await RunCoreAsync(command, timeout, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
