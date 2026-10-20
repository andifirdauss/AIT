using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Verification;

public sealed partial class BuildRunner
{
    private async Task KillAsync(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            using var wait = new CancellationTokenSource(KillWait);
            await process.WaitForExitAsync(wait.Token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not stop build process {ProcessId}", SafeId(process));
        }
    }
}
