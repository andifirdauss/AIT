using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AIThread.Service.Verification;

namespace AIThread.Service.Workspace.Commands;

public sealed partial class CommandRunner
{
    public async Task<CommandResult> RunAsync(string command, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var bash = Bash.Value;
        var startInfo = new ProcessStartInfo(bash ?? "cmd.exe")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (bash is not null)
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            // cmd.exe parses its own command line; the command goes in as written.
            startInfo.Arguments = $"/d /s /c \"{command}\"";
        }

        BuildRunner.PrepareEnvironment(startInfo.Environment);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["PAGER"] = "cat";
        startInfo.Environment["TERM"] = "dumb";
        // Git Bash keeps the Windows PATH, so curl, node, dotnet and the rest are found as in a terminal.
        if (bash is not null) startInfo.Environment["MSYS_NO_PATHCONV"] = "1";

        var shell = bash is null ? "cmd.exe" : "bash";
        var clock = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new CommandResult(command, workingDirectory, shell, null, false, false, string.Empty, 0, $"{shell} could not start: {ex.Message}");
        }
        process.StandardInput.Close();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        var stdout = ReadBoundedAsync(process.StandardOutput, limit.Token);
        var stderr = ReadBoundedAsync(process.StandardError, limit.Token);
        var timedOut = false;
        try
        {
            var (output, truncatedOut) = await stdout;
            if (truncatedOut) Kill(process);
            var (errors, truncatedErr) = await stderr;
            if (truncatedErr) Kill(process);
            await process.WaitForExitAsync(limit.Token);
            return Result(output, errors, truncatedOut || truncatedErr, process.HasExited ? process.ExitCode : null);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
            timedOut = true;
        }

        var partial = stdout.IsCompletedSuccessfully ? stdout.Result.Text : string.Empty;
        var partialErrors = stderr.IsCompletedSuccessfully ? stderr.Result.Text : string.Empty;
        return Result(partial, partialErrors, false, null);

        CommandResult Result(string output, string errors, bool truncated, int? exitCode)
        {
            var text = new StringBuilder(TextFileIo.NormalizeNewLines(output).TrimEnd());
            if (errors.Trim().Length > 0)
            {
                if (text.Length > 0) text.Append("\n\n");
                text.Append("[stderr]\n").Append(TextFileIo.NormalizeNewLines(errors).TrimEnd());
            }
            var combined = text.ToString();
            var cut = combined.Length > MaxOutputChars;
            if (cut) combined = combined[..MaxOutputChars];
            return new CommandResult(command, workingDirectory, shell, exitCode, timedOut, truncated || cut, combined, clock.ElapsedMilliseconds);
        }
    }

    // Stops reading once the output is longer than a tool result can hold; the caller then stops the process.
    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            builder.Append(buffer, 0, read);
            if (builder.Length > MaxOutputChars) return (builder.ToString(), true);
        }
        return (builder.ToString(), false);
    }

    private static void Kill(Process process)
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
