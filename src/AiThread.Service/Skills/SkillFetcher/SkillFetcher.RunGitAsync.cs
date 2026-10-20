using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace AiThread.Service.Skills;

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
            throw new InvalidOperationException("git tidak ditemukan di PATH. Pasang Git, atau pakai link GitHub/GitLab yang bisa diunduh sebagai ZIP.");
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
            throw new TimeoutException("git clone melebihi 5 menit.");
        }

        if (process.ExitCode != 0)
        {
            var error = (await stderr).Trim();
            throw new InvalidOperationException($"git clone gagal: {(error.Length > 400 ? error[..400] : error)}");
        }
    }
}
