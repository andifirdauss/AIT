using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

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
                activityLog.Info(Category, Lang.T("9router yang dijalankan AIT dihentikan.", "The 9router started by AIT was stopped."));
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
