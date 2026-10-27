using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

// Runs the CLI of a git host (gh for GitHub, glab for GitLab) for GitPublisher: fixed arguments, no shell, never a
// prompt (stdin is the given text or closed at once), and a time limit.
public static class ForgeCli
{
    // The executable on PATH; on Windows only an .exe (a .cmd would go through cmd.exe and its quoting).
    public static string? Find(string name)
    {
        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(folder.Trim().Trim('"'), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }
        return null;
    }

    public static async Task<GitResult> RunAsync(string executable, string directory, IReadOnlyList<string> arguments, string? input, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        startInfo.Environment["GH_PROMPT_DISABLED"] = "1";
        startInfo.Environment["NO_PROMPT"] = "1";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";
        startInfo.Environment["NO_COLOR"] = "1";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or DirectoryNotFoundException)
        {
            return new GitResult(string.Empty, $"{Path.GetFileNameWithoutExtension(executable)} could not be started: {ex.Message}");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), limit.Token);
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(limit.Token);
            var stderr = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            var output = await stdout;
            if (process.ExitCode == 0) return new GitResult(output, null);
            var message = (await stderr).Trim();
            return new GitResult(output, message.Length == 0 ? $"exited with code {process.ExitCode}." : message);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new GitResult(string.Empty, $"stopped after {timeout.TotalSeconds:0} seconds.");
        }
    }
}
