using System.ComponentModel;
using System.Diagnostics;

namespace AIThread.Service.Testing;

// An app AIT started for a test (IAppLauncher). Disposing it stops the process with everything it started (dotnet run →
// the app, cmd → npm → node), so nothing outlives the test.
public sealed class RunningApp(Process process, AppLaunchCommand command) : IAsyncDisposable
{
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(10);

    public AppLaunchCommand Command { get; } = command;

    public int ProcessId { get; } = process.Id;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var wait = new CancellationTokenSource(KillWait);
                await process.WaitForExitAsync(wait.Token);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            // Already gone, or it does not stop in time; there is nothing more to do here.
        }
        finally
        {
            process.Dispose();
        }
    }
}
