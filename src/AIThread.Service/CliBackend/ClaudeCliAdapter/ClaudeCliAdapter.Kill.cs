using System.ComponentModel;
using System.Diagnostics;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited, or never started.
        }
    }
}
