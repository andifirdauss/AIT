using System.ComponentModel;
using System.Diagnostics;
using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    private async Task<CliBackendStatusDto> ProbeAsync(CancellationToken cancellationToken)
    {
        var launch = ResolveLaunch();
        if (launch is null) return new CliBackendStatusDto(CliModelId.Claude, DisplayName, false, null, null, null);

        var startInfo = new ProcessStartInfo(launch.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8
        };
        foreach (var argument in launch.Prefix) startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add("--version");
        CleanEnvironment(startInfo.Environment);

        using var process = new Process { StartInfo = startInfo };
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(VersionTimeout);
        try
        {
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            var version = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return process.ExitCode == 0
                ? new CliBackendStatusDto(CliModelId.Claude, DisplayName, true, launch.Display, version, null)
                : new CliBackendStatusDto(CliModelId.Claude, DisplayName, true, launch.Display, null,
                    Lang.T($"`claude --version` keluar dengan kode {process.ExitCode}.", $"`claude --version` exited with code {process.ExitCode}."));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            return new CliBackendStatusDto(CliModelId.Claude, DisplayName, true, launch.Display, null,
                Lang.T("`claude --version` tidak menjawab.", "`claude --version` did not answer."));
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "Could not start Claude Code CLI at {Path}.", launch.Display);
            return new CliBackendStatusDto(CliModelId.Claude, DisplayName, false, launch.Display, null, ex.Message);
        }
    }
}
