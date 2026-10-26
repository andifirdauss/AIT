using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace AIThread.Service.Workspace;

// Output of one git run: stdout as is (NUL separators of -z kept), or Error with git's message when it failed.
public sealed record GitResult(string Output, string? Error)
{
    public bool Ok => Error is null;
}

// Runs git for AIT itself (GitChangeTracker): fixed arguments, no shell, never a prompt, and never a write to .git
// unless the caller asks for one (fetch). Lines that need quoting come back raw (core.quotepath=false).
public static class GitProcess
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] GlobalArguments =
        ["--no-pager", "-c", "color.ui=never", "-c", "core.quotepath=false", "-c", "log.showSignature=false"];

    public static async Task<GitResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken,
        string? input = null, TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (input is not null) startInfo.StandardInputEncoding = new UTF8Encoding(false);
        foreach (var argument in GlobalArguments.Concat(arguments)) startInfo.ArgumentList.Add(argument);
        // A read must not refresh the index, and nothing may wait for a credential.
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or DirectoryNotFoundException)
        {
            return new GitResult(string.Empty, "git is not installed or the folder does not exist.");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout ?? DefaultTimeout);
        try
        {
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), limit.Token);
                process.StandardInput.Close();
            }
            var stdout = process.StandardOutput.ReadToEndAsync(limit.Token);
            var stderr = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            var output = await stdout;
            if (process.ExitCode == 0) return new GitResult(output, null);
            var message = (await stderr).Trim();
            return new GitResult(output, message.Length == 0 ? $"git exited with code {process.ExitCode}." : message);
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
            return new GitResult(string.Empty, "git took too long.");
        }
    }

    // Lines of an output, without the empty last one.
    public static IReadOnlyList<string> Lines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();

    // Entries of a -z output.
    public static IReadOnlyList<string> Entries(string output) => output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
}
