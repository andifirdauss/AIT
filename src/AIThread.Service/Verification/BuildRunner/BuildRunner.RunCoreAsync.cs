using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Verification;

public sealed partial class BuildRunner
{
    private async Task<BuildResult> RunCoreAsync(BuildCommand command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var parser = new BuildOutputParser(command.WorkingDirectory);
        var tail = new Queue<string>();
        void OnLine(string line)
        {
            lock (tail)
            {
                parser.Add(line);
                tail.Enqueue(line);
                if (tail.Count > TailLines) tail.Dequeue();
            }
        }

        var startInfo = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in command.Arguments) startInfo.ArgumentList.Add(argument);
        PrepareEnvironment(startInfo.Environment);

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Build command {Command} could not start", command.Display);
            return new BuildResult(command.Display, false, false, null, [], 0, string.Empty, 0,
                $"'{command.FileName}' could not be started ({ex.Message}). Is the {command.Stack} toolchain installed and on PATH?");
        }

        // Nothing may wait for keyboard input.
        process.StandardInput.Close();
        var pumps = Task.WhenAll(PumpAsync(process.StandardOutput, OnLine), PumpAsync(process.StandardError, OnLine));

        var timedOut = false;
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            limit.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                await KillAsync(process);
                cancellationToken.ThrowIfCancellationRequested();
                timedOut = true;
            }
        }

        try
        {
            await pumps.WaitAsync(DrainGrace, CancellationToken.None);
        }
        catch (TimeoutException)
        {
            // See DrainGrace.
        }
        stopwatch.Stop();

        int? exitCode = process.HasExited ? process.ExitCode : null;
        string outputTail;
        lock (tail) outputTail = string.Join('\n', tail);

        var succeeded = !timedOut && exitCode == 0;
        var errors = parser.Errors.ToList();
        return new BuildResult(command.Display, succeeded, timedOut, exitCode,
            errors, parser.WarningCount, outputTail, stopwatch.ElapsedMilliseconds, succeeded || timedOut ? null : MissingToolchain(command, errors));
    }
}
