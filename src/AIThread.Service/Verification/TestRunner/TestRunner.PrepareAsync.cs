using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Verification;

public sealed partial class TestRunner
{
    // Runs the step before the tests (the MSBuild build of a test project dotnet cannot build). Null when it succeeded;
    // otherwise the result of the whole run: no test ran, and the output tail says why (a restore that needs credentials,
    // a compile error).
    private async Task<TestRunResult?> PrepareAsync(TestCommand command, TestPrepare prepare, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tail = new Queue<string>();
        void OnLine(string line)
        {
            lock (tail)
            {
                tail.Enqueue(line);
                if (tail.Count > TailLines) tail.Dequeue();
            }
        }

        var startInfo = new ProcessStartInfo(prepare.FileName)
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
        foreach (var argument in prepare.Arguments) startInfo.ArgumentList.Add(argument);
        BuildRunner.PrepareEnvironment(startInfo.Environment);

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Test build step {Command} could not start", prepare.Display);
            return new TestRunResult(command, false, null, [], string.Empty, 0,
                $"'{prepare.FileName}' tidak bisa dijalankan ({ex.Message}). Apakah Visual Studio atau Build Tools terpasang?");
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
            // Build servers may keep the pipes open; what the build wrote is read.
        }
        stopwatch.Stop();

        int? exitCode = process.HasExited ? process.ExitCode : null;
        if (!timedOut && exitCode == 0) return null;

        string outputTail;
        lock (tail) outputTail = string.Join('\n', tail);
        logger.LogInformation("Test build step {Command} failed (exit code {ExitCode})", prepare.Display, exitCode);
        return new TestRunResult(command, timedOut, exitCode, [], outputTail, stopwatch.ElapsedMilliseconds, null, $"`{prepare.Display}` gagal sebelum test bisa dijalankan");
    }
}
