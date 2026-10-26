using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AIThread.Common.Localization;

namespace AIThread.Service.Skills;

public sealed partial class SkillFetcher
{
    private static async Task RunGitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        // Never wait for a credential prompt: private repositories simply fail.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException(Lang.T("git tidak ditemukan di PATH. Pasang Git, atau pakai link GitHub/GitLab yang bisa diunduh sebagai ZIP.", "git was not found on PATH. Install Git, or use a GitHub/GitLab link that can be downloaded as a ZIP."));
        }

        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(GitTimeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(Lang.T("git clone melebihi 5 menit.", "git clone took longer than 5 minutes."));
        }

        if (process.ExitCode != 0)
        {
            var error = (await stderr).Trim();
            throw new InvalidOperationException(Lang.T($"git clone gagal: {(error.Length > 400 ? error[..400] : error)}", $"git clone failed: {(error.Length > 400 ? error[..400] : error)}"));
        }
    }
}
