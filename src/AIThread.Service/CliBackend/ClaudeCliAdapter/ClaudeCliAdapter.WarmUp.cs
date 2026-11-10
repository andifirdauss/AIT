using Microsoft.Extensions.Logging;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // Starts the process the next call with this model and system prompt will use, next to the current call: a node
    // calls the same CLI several times in one thread. A process that cannot start only means the next call starts its own.
    private void WarmUp(CliLaunch launch, string model, string system, string key)
    {
        if (options.Value.ClaudeCli.WarmProcesses <= 0 || _warm.Has(key)) return;
        _ = Task.Run(async () =>
        {
            var work = CliProcess.NewWorkFolder();
            try
            {
                var systemFile = await WriteSystemFileAsync(work, system, CancellationToken.None);
                _warm.Add(key, new WarmCliProcess(CliProcess.Start(launch, BuildArguments(model, systemFile), work, CleanEnvironment, NotInstalled), work));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Claude Code CLI could not be started ahead of its call.");
                CliProcess.DeleteQuietly(work);
            }
        });
    }
}
