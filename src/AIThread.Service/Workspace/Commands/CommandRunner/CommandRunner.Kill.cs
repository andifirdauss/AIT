using System.ComponentModel;
using System.Diagnostics;

namespace AIThread.Service.Workspace.Commands;

public sealed partial class CommandRunner
{
    // Stops the shell and everything it started (a server under `npm run dev` is a grandchild).
    internal static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited.
        }
    }
}
