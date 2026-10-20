using AiThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    public void StopStarted()
    {
        var process = Interlocked.Exchange(ref _started, null);
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                activityLog.Info(Category, "9router yang dijalankan AIT dihentikan.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Stopping 9router failed.");
        }
        finally
        {
            process.Dispose();
        }
    }
}
