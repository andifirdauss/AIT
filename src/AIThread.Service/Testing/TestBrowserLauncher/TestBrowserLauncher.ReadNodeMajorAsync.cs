using System.ComponentModel;
using System.Diagnostics;

namespace AIThread.Service.Testing;

public sealed partial class TestBrowserLauncher
{
    // Major version from `node --version` ("v24.18.0" → 24); null when it does not run or answer in time.
    private static async Task<int?> ReadNodeMajorAsync(string path, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(path, "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        try
        {
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var major = output.Trim().TrimStart('v').Split('.')[0];
            return process.ExitCode == 0 && int.TryParse(major, out var value) ? value : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
