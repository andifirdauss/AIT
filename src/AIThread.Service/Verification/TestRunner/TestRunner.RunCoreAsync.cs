using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Verification;

public sealed partial class TestRunner
{
    private async Task<TestRunResult> RunCoreAsync(TestCommand command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var keepOutput = command.Suite.Format is TestReportFormat.GoJson or TestReportFormat.RustText;
        var output = new StringBuilder();
        var tail = new Queue<string>();
        void OnLine(string line)
        {
            lock (tail)
            {
                if (keepOutput && output.Length < MaxOutputChars) output.Append(line).Append('\n');
                tail.Enqueue(line);
                if (tail.Count > TailLines) tail.Dequeue();
            }
        }

        var startInfo = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = command.Suite.Directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in command.Arguments) startInfo.ArgumentList.Add(argument);
        BuildRunner.PrepareEnvironment(startInfo.Environment);
        // Runners that look for a CI flag skip watch mode and interactive prompts.
        startInfo.Environment["CI"] = "true";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        var started = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Test command {Command} could not start", command.Display);
            return new TestRunResult(command, false, null, [], string.Empty, 0,
                $"'{command.FileName}' tidak bisa dijalankan ({ex.Message}). Apakah toolchain {command.Suite.Stack} terpasang dan ada di PATH?");
        }

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
            // Child processes (a build server, a browser) may keep the pipes open; what the runner wrote is read.
        }
        stopwatch.Stop();

        int? exitCode = process.HasExited ? process.ExitCode : null;
        string outputTail, fullOutput;
        lock (tail)
        {
            outputTail = string.Join('\n', tail);
            fullOutput = output.ToString();
        }

        var (outcomes, reportError) = TestResultParser.Parse(command, fullOutput, started);
        return new TestRunResult(command, timedOut, exitCode, outcomes, outputTail, stopwatch.ElapsedMilliseconds, null, outcomes.Count == 0 ? reportError : null);
    }
}
