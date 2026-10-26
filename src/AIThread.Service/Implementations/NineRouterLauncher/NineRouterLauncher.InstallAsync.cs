using System.Diagnostics;
using System.Text;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    // npm is a .cmd script, so it runs through cmd. Its output is kept for the error message.
    private async Task<string?> InstallAsync(string node, IReadOnlyList<string> dirs, CancellationToken cancellationToken)
    {
        var nodeDir = Path.GetDirectoryName(node)!;
        var npm = NineRouterLocator.FindExecutable("npm.cmd", null, dirs.Prepend(nodeDir), File.Exists);
        if (npm is null) return Lang.T("npm tidak ditemukan; pasang ulang Node.js (npm ikut terpasang), lalu coba lagi.", "npm was not found; reinstall Node.js (npm comes with it), then try again.");

        activityLog.Info(Category, Lang.T("9router tidak ditemukan; memasang dengan npm install -g 9router …", "9router was not found; installing with npm install -g 9router …"));
        var startInfo = Shell($"\"{npm}\" install -g {NineRouterLocator.PackageName} --no-fund --no-audit", nodeDir, node);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("cmd.exe did not start.");
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, Settings.InstallTimeoutSeconds)));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            return Lang.T($"npm install -g 9router tidak selesai dalam {Settings.InstallTimeoutSeconds} detik.", $"npm install -g 9router did not finish within {Settings.InstallTimeoutSeconds} seconds.");
        }

        string text;
        lock (output) text = output.ToString();
        logger.LogInformation("npm install -g 9router exited {ExitCode}: {Output}", process.ExitCode, text);
        return process.ExitCode == 0 ? null : Lang.T($"npm install -g 9router gagal (exit {process.ExitCode}). {Tail(text)}", $"npm install -g 9router failed (exit {process.ExitCode}). {Tail(text)}").TrimEnd();
    }
}
